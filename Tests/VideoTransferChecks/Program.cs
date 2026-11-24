using System.Security.Cryptography;
using RemoteDebugServer.Models;
using RemoteDebugServer.Services;

string root = Path.Combine(Path.GetTempPath(), "RemoteDebuggerVideoTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
try
{
    byte[] content = RandomNumberGenerator.GetBytes(700003);
    string hash = Convert.ToHexString(SHA256.HashData(content));
    string id = Guid.NewGuid().ToString("N");
    using (var receiver = new VideoTransferReceiver())
    {
        for (int offset = 0; offset < content.Length; offset += 262144)
        {
            int count = Math.Min(262144, content.Length - offset);
            string? result = await receiver.ReceiveAsync(new VideoChunk { TransferId = id, FileName = "recording_test.mp4", TotalBytes = content.Length,
                Offset = offset, Data = Convert.ToBase64String(content, offset, count) }, root);
            Check(result == null && Directory.GetFiles(root, "*.mp4").Length == 0, "incomplete file remains unpublished");
        }
        string? final = await receiver.ReceiveAsync(new VideoChunk { TransferId = id, TotalBytes = content.Length, Offset = content.Length, Complete = true, Sha256 = hash }, root);
        Check(final != null && File.ReadAllBytes(final).SequenceEqual(content), "multi-chunk byte-perfect roundtrip");
        Check(Directory.GetFiles(root, "*.partial").Length == 0, "no partial after success");
    }
    await Reject("invalid hash", async receiver => {
        var chunk = First(); await receiver.ReceiveAsync(chunk, root);
        await receiver.ReceiveAsync(new VideoChunk { TransferId = chunk.TransferId, TotalBytes = 3, Offset = 3, Complete = true, Sha256 = new string('0', 64) }, root);
    });
    await Reject("out-of-order chunk", async receiver => {
        var chunk = First(); await receiver.ReceiveAsync(chunk, root);
        chunk.Offset = 1; await receiver.ReceiveAsync(chunk, root);
    });
    await Reject("cross-transfer mix", async receiver => {
        await receiver.ReceiveAsync(First(), root); await receiver.ReceiveAsync(First(), root);
    });
    await Reject("path traversal", async receiver => { var c = First(); c.FileName = "../escape.mp4"; await receiver.ReceiveAsync(c, root); });
    await Reject("oversized chunk", async receiver => { var c = First(); c.Data = new string('A', 349529); await receiver.ReceiveAsync(c, root); });
    await Reject("premature completion", async receiver => {
        var c = First(); c.TotalBytes = 4; await receiver.ReceiveAsync(c, root);
        c.Offset = 3; c.Data = ""; c.Complete = true; await receiver.ReceiveAsync(c, root);
    });
    using (var interrupted = new VideoTransferReceiver()) { await interrupted.ReceiveAsync(First(), root); }
    Check(Directory.GetFiles(root, "*.partial").Length == 0, "disconnect cleans incomplete upload");
    using (var receiver = new VideoTransferReceiver())
    {
        var c = First(); await receiver.ReceiveAsync(c, root);
        c.Offset = 3; c.Data = ""; c.Complete = true; c.Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));
        Check(await receiver.ReceiveAsync(c, root) != null, "retry after interruption succeeds");
    }
    string local = Path.Combine(root, "current.mp4");
    string older = Path.Combine(root, "older.mp4");
    await File.WriteAllBytesAsync(local, content);
    await File.WriteAllBytesAsync(older, new byte[] { 9 });
    var delivery = new RemoteDebugger.RecordedVideoDelivery();
    var confirmation = new TaskCompletionSource();
    var delivering = delivery.DeliverAsync(local, path => {
        Check(path == local, "upload targets the current recording");
        return confirmation.Task;
    });
    Check(File.Exists(local), "source remains while awaiting server confirmation");
    confirmation.SetResult();
    await delivering;
    Check(!File.Exists(local), "confirmed upload deletes local video");
    Check(File.Exists(older), "cleanup never deletes a different recording");

    await File.WriteAllBytesAsync(local, content);
    delivery = new RemoteDebugger.RecordedVideoDelivery();
    bool uploadFailed = false;
    try { await delivery.DeliverAsync(local, _ => Task.FromException(new IOException("server rejected checksum"))); }
    catch (IOException) { uploadFailed = true; }
    Check(uploadFailed && File.Exists(local), "rejected/unconfirmed upload retains original");
    int retries = 0;
    await delivery.DeliverAsync(local, _ => { retries++; return Task.CompletedTask; });
    Check(retries == 1 && !File.Exists(local), "retry delivers then deletes the same file");

    bool missingRejected = false;
    try { await new RemoteDebugger.RecordedVideoDelivery().DeliverAsync(local, _ => throw new Exception("must not upload another video")); }
    catch (FileNotFoundException) { missingRejected = true; }
    Check(missingRejected && File.Exists(older), "missing current file never falls back to an older video");

    if (OperatingSystem.IsWindows())
    {
        await File.WriteAllBytesAsync(local, content);
        delivery = new RemoteDebugger.RecordedVideoDelivery();
        int uploads = 0;
        bool cleanupFailed = false;
        using (var locked = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { await delivery.DeliverAsync(local, _ => { uploads++; return Task.CompletedTask; }); }
            catch (IOException) { cleanupFailed = true; }
            Check(cleanupFailed && File.Exists(local), "cleanup failure is reported and file retained");
        }
        await delivery.DeliverAsync(local, _ => { uploads++; return Task.CompletedTask; });
        Check(uploads == 1 && !File.Exists(local), "cleanup retry does not duplicate a confirmed upload");
    }
    Console.WriteLine($"PASS: {passed} video transfer and cleanup assertions");
}
finally
{
    // Only files created by this test, under its unique temporary directory.
    foreach (string file in Directory.GetFiles(root)) File.Delete(file);
    Directory.Delete(root);
}
VideoChunk First() => new() { TransferId = Guid.NewGuid().ToString("N"), FileName = "recording_retry.mp4", TotalBytes = 3, Data = "AQID" };
void Check(bool success, string message) { if (!success) throw new Exception(message); passed++; }
async Task Reject(string label, Func<VideoTransferReceiver, Task> action)
{
    bool rejected = false;
    using (var receiver = new VideoTransferReceiver())
    {
        try { await action(receiver); } catch (InvalidDataException) { rejected = true; }
    }
    Check(rejected, label);
    Check(Directory.GetFiles(root, "*.partial").Length == 0, "cleanup: " + label);
}

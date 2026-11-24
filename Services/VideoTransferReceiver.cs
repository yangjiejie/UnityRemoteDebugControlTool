using System.IO;
using System.Security.Cryptography;
using RemoteDebugServer.Models;

namespace RemoteDebugServer.Services
{
    /// <summary>One bounded, sequential upload per connection; publish only after length/hash verification.</summary>
    internal sealed class VideoTransferReceiver : IDisposable
    {
        private FileStream? _stream;
        private IncrementalHash? _hash;
        private string? _id, _partialPath, _finalPath;
        private long _total;
        public async Task<string?> ReceiveAsync(VideoChunk chunk, string directory)
        {
            if (!Guid.TryParseExact(chunk.TransferId, "N", out _) || chunk.TotalBytes <= 0 ||
                chunk.TotalBytes > 32L * 1024 * 1024 * 1024 || chunk.Data.Length > 349528)
                throw new InvalidDataException("视频分块参数无效");
            if (_stream == null)
            {
                if (chunk.Offset != 0 || chunk.Complete || Path.GetFileName(chunk.FileName) != chunk.FileName ||
                    chunk.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    !chunk.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("视频文件名或首块无效");
                Directory.CreateDirectory(directory);
                _id = chunk.TransferId;
                _total = chunk.TotalBytes;
                _finalPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(chunk.FileName) + "_" + _id + ".mp4");
                _partialPath = _finalPath + ".partial";
                _stream = new FileStream(_partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            }
            if (_id != chunk.TransferId || _total != chunk.TotalBytes || _stream.Position != chunk.Offset)
                throw new InvalidDataException("视频分块顺序或长度不匹配，请重新传输");
            if (!chunk.Complete)
            {
                byte[] bytes = Convert.FromBase64String(chunk.Data);
                if (bytes.Length == 0 || bytes.Length > 262144 || bytes.Length > _total - chunk.Offset)
                    throw new InvalidDataException("视频分块大小无效");
                await _stream.WriteAsync(bytes);
                _hash!.AppendData(bytes);
                return null;
            }
            if (chunk.Data.Length != 0 || _stream.Position != _total ||
                !string.Equals(Convert.ToHexString(_hash!.GetHashAndReset()), chunk.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("视频校验失败，请重新传输");
            await _stream.FlushAsync();
            _stream.Dispose();
            _stream = null;
            File.Move(_partialPath!, _finalPath!);
            string result = _finalPath!;
            _partialPath = null;
            Dispose();
            return result;
        }
        public void Dispose()
        {
            _stream?.Dispose(); _stream = null;
            _hash?.Dispose(); _hash = null;
            if (_partialPath != null) { try { File.Delete(_partialPath); } catch { } }
            _partialPath = null;
        }
    }
}

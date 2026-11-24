namespace RemoteDebugServer.Models
{
    // Versioned separately from legacy whole-file transfers. Each message is <= 256 KiB of payload.
    public class VideoChunk
    {
        public string TransferId { get; set; } = "";
        public string FileName { get; set; } = "";
        public long TotalBytes { get; set; }
        public long Offset { get; set; }
        public string Data { get; set; } = "";
        public string Sha256 { get; set; } = ""; // Only present on the final, empty chunk.
        public bool Complete { get; set; }
    }

    public class RecordingStatusData
    {
        public int ProtocolVersion { get; set; }
        public string State { get; set; } = "";
        public string Error { get; set; } = "";
        public string OutputPath { get; set; } = "";
    }

    public class VideoTransferResult
    {
        public string TransferId { get; set; } = "";
        public bool Success { get; set; }
        public string Error { get; set; } = "";
    }
}

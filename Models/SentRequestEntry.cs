using System;

namespace RemoteDebugServer.Models
{
    /// <summary>
    /// 一条已发送的协议记录，用于「最近发送」列表并持久化到本地，
    /// 使工具重启后仍可查看 / 重发历史发包请求。
    /// </summary>
    public class SentRequestEntry
    {
        /// <summary>协议名（如 LoginNotify）。</summary>
        public string ProtocolName { get; set; } = "";

        /// <summary>协议消息号 MsgId。</summary>
        public short MsgId { get; set; }

        /// <summary>发送时刻的本地时间文本（HH:mm:ss）。</summary>
        public string TimeText { get; set; } = "";

        /// <summary>发送时刻（用于跨会话排序 / 展示）。</summary>
        public DateTime SentAt { get; set; }

        /// <summary>协议内容的单行摘要（已截断）。</summary>
        public string Summary { get; set; } = "";

        /// <summary>原始 Protobuf 字节。</summary>
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();

        /// <summary>RawBytes 的 Base64 形式，便于序列化与调试查看。</summary>
        public string Base64 { get; set; } = "";

        /// <summary>是否被重发过（用于列表中标记）。</summary>
        public bool Resent { get; set; }

        /// <summary>最近一次重发的时刻。</summary>
        public DateTime ResentAt { get; set; }
    }
}

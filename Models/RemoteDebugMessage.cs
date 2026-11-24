using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteDebugServer.Models
{
    #region 消息类型枚举

    public enum MessageType
    {
        Register,
        Heartbeat,
        Log,
        LogBatch,
        SceneTree,
        Command,
        CommandResult,
        FileTransfer,
        VideoChunk,
        VideoTransferResult,
        FileRequest,
        Disconnect,
        MockProtocol,        // 模拟游戏服务器下发的 SC 协议
        UpdateDeviceInfo,    // 客户端更新设备信息（如热更后版本号变化）
    }

    #endregion

    #region 基础消息

    public class DebugMessage
    {
        public string Type { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public long Timestamp { get; set; }
        public string JsonData { get; set; } = "";

        public DebugMessage() { }

        public DebugMessage(MessageType type, string deviceId, string deviceName)
        {
            Type = type.ToString();
            DeviceId = deviceId;
            DeviceName = deviceName;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    #endregion

    #region 注册

    public class RegisterData
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Platform { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public string UnityVersion { get; set; } = "";
        /// <summary>资源热更新版本号，对应客户端 remoteConfig.version（JSON 键 hotUpdateVersion）。</summary>
        public string HotUpdateVersion { get; set; } = "";
        public long StartTime { get; set; }
    }

    /// <summary>
    /// 设备信息更新（热更后版本号等字段变化时由客户端主动推送）
    /// </summary>
    public class UpdateDeviceInfoData : RegisterData
    {
    }

    #endregion

    #region 日志

    public class LogEntry
    {
        public string Condition { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public int LogType { get; set; }
        public long Timestamp { get; set; }
    }

    public class LogBatchData
    {
        public List<LogEntry> Logs { get; set; } = new();
    }

    #endregion

    #region 场景树

    public class SceneInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsLoaded { get; set; }
        public int BuildIndex { get; set; }
        public List<SceneTreeNode> RootObjects { get; set; } = new();
    }

    public class SceneTreeNode
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public int InstanceId { get; set; }
        public bool ActiveInHierarchy { get; set; }
        public bool ActiveSelf { get; set; }
        public string Tag { get; set; } = "";
        public int Layer { get; set; }
        public List<string> Components { get; set; } = new();
        public List<SceneTreeNode> Children { get; set; } = new();
        public TransformData? Transform { get; set; }
    }

    public class TransformData
    {
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float RotX { get; set; }
        public float RotY { get; set; }
        public float RotZ { get; set; }
        public float ScaleX { get; set; }
        public float ScaleY { get; set; }
        public float ScaleZ { get; set; }
    }

    public class SceneTreeData
    {
        public List<SceneInfo> Scenes { get; set; } = new();
    }

    #endregion

    #region 指令

    public class RemoteCommand
    {
        public string CommandId { get; set; } = "";
        public string Command { get; set; } = "";
        public string Parameters { get; set; } = "";
    }

    public class CommandResult
    {
        public string CommandId { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Data { get; set; } = "";
    }

    #endregion

    #region 文件

    public class FileTransferData
    {
        public string FileName { get; set; } = "";
        public long FileSize { get; set; }
        public string FileData { get; set; } = "";
        public string FileType { get; set; } = "";
        public string Description { get; set; } = "";
        public string RelativePath { get; set; } = ""; // 热更新时：目标目录下的相对路径
    }

    public class FileRequestData
    {
        public string FilePath { get; set; } = "";
        public string FileType { get; set; } = "";
        public bool Recursive { get; set; }
        public string Wildcard { get; set; } = "";
    }

    #endregion

    #region 模拟协议（模拟游戏服务器下发）

    /// <summary>
    /// 模拟游戏服务器下发的 SC 协议数据。
    /// MsgId 为 SC 协议号；Data 为 Base64 编码的 protobuf 字节。
    /// 注意：服务端直接将 protobuf 字节交给客户端，由客户端注入 NetEvent.OnLuaMessage，
    /// 因此工具侧无需再做游戏网络层的加密/分包。
    /// </summary>
    public class MockProtocolData
    {
        public short MsgId { get; set; }
        public string Data { get; set; } = "";
    }

    #endregion

    #region 协议编解码

    public static class MessageCodec
    {
        public const int HeaderSize = 4;

        public static byte[] Encode(DebugMessage message)
        {
            string json = JsonSerializer.Serialize(message, JsonOptions.Default);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] packet = new byte[HeaderSize + jsonBytes.Length];

            BitConverter.TryWriteBytes(packet.AsSpan(0, 4), jsonBytes.Length);
            Buffer.BlockCopy(jsonBytes, 0, packet, HeaderSize, jsonBytes.Length);

            return packet;
        }

        public static async Task<DebugMessage?> DecodeAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            byte[] headerBuffer = new byte[HeaderSize];
            await ReadExactAsync(stream, headerBuffer, 0, HeaderSize, cancellationToken);
            int messageLength = BitConverter.ToInt32(headerBuffer, 0);

            if (messageLength <= 0 || messageLength > 50 * 1024 * 1024)
                throw new InvalidDataException($"无效的消息长度: {messageLength}");

            byte[] messageBuffer = new byte[messageLength];
            await ReadExactAsync(stream, messageBuffer, 0, messageLength, cancellationToken);

            string json = Encoding.UTF8.GetString(messageBuffer);
            return JsonSerializer.Deserialize<DebugMessage>(json, JsonOptions.Default);
        }

        public static DebugMessage CreateMessage<T>(MessageType type, string deviceId, string deviceName, T data)
        {
            var msg = new DebugMessage(type, deviceId, deviceName);
            msg.JsonData = JsonSerializer.Serialize(data, JsonOptions.Default);
            return msg;
        }

        public static T? ParseData<T>(this DebugMessage message)
        {
            if (string.IsNullOrEmpty(message.JsonData))
                return default;
            return JsonSerializer.Deserialize<T>(message.JsonData, JsonOptions.Default);
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
                if (bytesRead == 0)
                    throw new EndOfStreamException("连接已关闭");
                totalRead += bytesRead;
            }
        }
    }

    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
        };
    }

    #endregion
}

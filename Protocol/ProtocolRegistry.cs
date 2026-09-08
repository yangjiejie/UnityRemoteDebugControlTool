using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using HotFix.Ctrl;

namespace RemoteDebugServer.Protocol
{
    /// <summary>
    /// 协议元信息：某条 SC 协议对应的 msgId、消息类名、以及 protobuf 解析器。
    /// </summary>
    public class ProtocolInfo
    {
        public short MsgId { get; set; }
        public string Name { get; set; } = "";
        public Type MessageType { get; set; } = null!;
        public MessageParser Parser { get; set; } = null!;
        public string Display => $"{Name}  ({MsgId})";
    }

    /// <summary>
    /// 通过反射在 HotFix.Ctrl 程序集中构建 msgId -> 协议信息 的注册表。
    /// 仅包含以 SC 开头的“服务器->客户端”协议（我们要“模拟服务器下发”）。
    /// </summary>
    public static class ProtocolRegistry
    {
        public static IReadOnlyList<ProtocolInfo> ServerToClientProtocols { get; }
        public static IReadOnlyDictionary<short, ProtocolInfo> ByMsgId { get; }

        static ProtocolRegistry()
        {
            var list = new List<ProtocolInfo>();
            var byMsgId = new Dictionary<short, ProtocolInfo>();

            var msgIdsType = typeof(MsgIds);
            var ctrlAsm = typeof(SCActivities).Assembly;

            foreach (var field in msgIdsType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(short))
                    continue;

                var name = field.Name;
                if (!name.StartsWith("SC", StringComparison.Ordinal))
                    continue;

                var type = ctrlAsm.GetType("HotFix.Ctrl." + name);
                if (type == null)
                    continue;

                var parserProp = type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static);
                if (parserProp == null)
                    continue;

                if (parserProp.GetValue(null) is not MessageParser parser)
                    continue;

                var msgId = (short)field.GetValue(null)!;
                var info = new ProtocolInfo
                {
                    MsgId = msgId,
                    Name = name,
                    MessageType = type,
                    Parser = parser,
                };

                list.Add(info);
                byMsgId[msgId] = info;
            }

            ServerToClientProtocols = list;
            ByMsgId = byMsgId;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using RemoteDebugServer.Models;

namespace RemoteDebugServer.Helpers
{
    /// <summary>
    /// 根据 ProtoFieldNode 树构建 protobuf IMessage，并递归生成 UI 字段树。
    /// 通过 FieldDescriptor.Accessor 进行反射读写（IMessage 本身不提供索引器）。
    /// </summary>
    public static class ProtoMessageBuilder
    {
        /// <summary>根据消息描述符构建用于绘制的字段树（根节点）。</summary>
        public static ProtoFieldNode CreateTree(MessageDescriptor desc)
        {
            var root = new ProtoFieldNode
            {
                Name = desc.Name,
                TypeLabel = "message",
            };
            foreach (var fd in desc.Fields.InFieldNumberOrder())
                root.Items.Add(CreateFieldNode(fd));
            return root;
        }

        /// <summary>生成一个 repeated 字段的“单个元素”节点（非 repeated 形态）。</summary>
        public static ProtoFieldNode CreateRepeatedItem(FieldDescriptor fd)
        {
            var node = new ProtoFieldNode
            {
                Descriptor = fd,
                Name = fd.Name,
                IsRepeated = false,
                IsMessage = fd.FieldType == FieldType.Message,
                IsEnum = fd.FieldType == FieldType.Enum,
                TypeLabel = BuildTypeLabel(fd),
            };

            if (fd.FieldType == FieldType.Enum)
            {
                foreach (var v in fd.EnumType.Values)
                    node.EnumValues.Add(v.Name);
                node.Value = fd.EnumType.Values.Count > 0 ? fd.EnumType.Values[0].Name : "";
            }
            else if (fd.FieldType == FieldType.Message)
            {
                foreach (var sub in fd.MessageType.Fields.InFieldNumberOrder())
                    node.Items.Add(CreateFieldNode(sub));
            }
            else
            {
                node.Value = DefaultScalar(fd.FieldType);
            }

            return node;
        }

        private static ProtoFieldNode CreateFieldNode(FieldDescriptor fd)
        {
            var node = new ProtoFieldNode
            {
                Descriptor = fd,
                Name = fd.Name,
                IsRepeated = fd.IsRepeated,
                IsMessage = fd.FieldType == FieldType.Message,
                IsEnum = fd.FieldType == FieldType.Enum,
                TypeLabel = BuildTypeLabel(fd),
            };

            if (fd.FieldType == FieldType.Enum)
            {
                foreach (var v in fd.EnumType.Values)
                    node.EnumValues.Add(v.Name);
                node.Value = fd.EnumType.Values.Count > 0 ? fd.EnumType.Values[0].Name : "";
            }
            else if (fd.FieldType == FieldType.Message)
            {
                // repeated message 由“添加”按钮生成元素；单 message 直接展开子字段
                if (!fd.IsRepeated)
                {
                    foreach (var sub in fd.MessageType.Fields.InFieldNumberOrder())
                        node.Items.Add(CreateFieldNode(sub));
                }
            }
            else
            {
                node.Value = DefaultScalar(fd.FieldType);
            }

            return node;
        }

        /// <summary>根据字段树构建 protobuf 消息。</summary>
        public static IMessage Build(MessageDescriptor desc, ProtoFieldNode root)
        {
            var msg = (IMessage)Activator.CreateInstance(desc.ClrType)!;

            foreach (var child in root.Items)
            {
                var fd = child.Descriptor!;
                if (child.IsMessage)
                {
                    if (child.IsRepeated)
                    {
                        var list = (IList)fd.Accessor.GetValue(msg)!;
                        foreach (var item in child.Items)
                            list.Add(Build(fd.MessageType, item));
                    }
                    else
                    {
                        if (HasValue(child))
                            fd.Accessor.SetValue(msg, Build(fd.MessageType, child));
                    }
                }
                else
                {
                    if (child.IsRepeated)
                    {
                        var list = (IList)fd.Accessor.GetValue(msg)!;
                        foreach (var item in child.Items)
                        {
                            if (HasValue(item))
                                list.Add(ConvertToFieldType(fd, item.Value));
                        }
                    }
                    else
                    {
                        if (HasValue(child))
                            fd.Accessor.SetValue(msg, ConvertToFieldType(fd, child.Value));
                    }
                }
            }

            return msg;
        }

        public static bool HasValue(ProtoFieldNode node)
        {
            if (node.IsMessage)
            {
                foreach (var c in node.Items)
                    if (HasValue(c)) return true;
                return false;
            }
            if (node.IsRepeated)
            {
                foreach (var c in node.Items)
                    if (HasValue(c)) return true;
                return false;
            }
            if (node.IsEnum)
                return !string.IsNullOrEmpty(node.Value);
            return !string.IsNullOrWhiteSpace(node.Value);
        }

        private static object ConvertToFieldType(FieldDescriptor fd, string? text)
        {
            text = (text ?? "").Trim();
            switch (fd.FieldType)
            {
                case FieldType.Double: return double.Parse(text, CultureInfo.InvariantCulture);
                case FieldType.Float: return float.Parse(text, CultureInfo.InvariantCulture);
                case FieldType.Int32:
                case FieldType.SInt32:
                case FieldType.SFixed32:
                case FieldType.UInt32:
                case FieldType.Fixed32:
                    return int.Parse(text, CultureInfo.InvariantCulture);
                case FieldType.Int64:
                case FieldType.SInt64:
                case FieldType.SFixed64:
                case FieldType.UInt64:
                case FieldType.Fixed64:
                    return long.Parse(text, CultureInfo.InvariantCulture);
                case FieldType.Bool: return bool.Parse(text);
                case FieldType.String: return text;
                case FieldType.Bytes: return ByteString.FromBase64(text);
                case FieldType.Enum:
                {
                    var enumType = fd.EnumType.ClrType;
                    if (string.IsNullOrEmpty(text)) return Enum.ToObject(enumType, 0);
                    return Enum.Parse(enumType, text);
                }
                default:
                    throw new NotSupportedException($"不支持的字段类型: {fd.FieldType}");
            }
        }

        private static string BuildTypeLabel(FieldDescriptor fd)
        {
            var baseType = fd.FieldType switch
            {
                FieldType.Double => "double",
                FieldType.Float => "float",
                FieldType.Int32 => "int32",
                FieldType.Int64 => "int64",
                FieldType.UInt32 => "uint32",
                FieldType.UInt64 => "uint64",
                FieldType.SInt32 => "sint32",
                FieldType.SInt64 => "sint64",
                FieldType.Fixed32 => "fixed32",
                FieldType.Fixed64 => "fixed64",
                FieldType.SFixed32 => "sfixed32",
                FieldType.SFixed64 => "sfixed64",
                FieldType.Bool => "bool",
                FieldType.String => "string",
                FieldType.Bytes => "bytes",
                FieldType.Enum => fd.EnumType.Name,
                FieldType.Message => fd.MessageType.Name,
                _ => fd.FieldType.ToString(),
            };
            return fd.IsRepeated ? $"repeated {baseType}" : baseType;
        }

        private static string DefaultScalar(FieldType type)
        {
            return type switch
            {
                FieldType.Bool => "false",
                _ => "",
            };
        }
    }
}

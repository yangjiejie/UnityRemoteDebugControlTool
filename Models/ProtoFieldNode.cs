using System.Collections.ObjectModel;
using System.ComponentModel;
using Google.Protobuf.Reflection;

namespace RemoteDebugServer.Models
{
    /// <summary>
    /// 递归表示一条协议消息的字段树，供 UI 通过反射绘制。
    /// </summary>
    public class ProtoFieldNode : INotifyPropertyChanged
    {
        /// <summary>对应的 protobuf 字段描述符；根节点为 null。</summary>
        public FieldDescriptor? Descriptor { get; set; }

        public string Name { get; set; } = "";
        public string TypeLabel { get; set; } = "";

        public bool IsRepeated { get; set; }

        /// <summary>字段类型为 message（可能是 repeated message）。</summary>
        public bool IsMessage { get; set; }

        /// <summary>字段类型为 enum。</summary>
        public bool IsEnum { get; set; }

        /// <summary>枚举值名称列表（仅 IsEnum 时有效）。</summary>
        public ObservableCollection<string> EnumValues { get; set; } = new();

        /// <summary>标量 / 枚举字段的编辑值（字符串形式）。</summary>
        public string Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 子节点集合：
        /// - 单个 message 字段：存放其所有子字段
        /// - repeated 字段：存放每个元素（元素本身再展开）
        /// - 标量 / 枚举字段：为空
        /// </summary>
        public ObservableCollection<ProtoFieldNode> Items { get; set; } = new();

        /// <summary>在 repeated 列表中的父节点，用于删除自身。</summary>
        public ProtoFieldNode? Parent { get; set; }

        private string _value = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

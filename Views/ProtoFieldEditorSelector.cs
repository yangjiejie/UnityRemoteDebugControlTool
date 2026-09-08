using System.Windows;
using System.Windows.Controls;
using Google.Protobuf.Reflection;
using RemoteDebugServer.Models;

namespace RemoteDebugServer
{
    /// <summary>根据字段节点类型选择值编辑器模板。</summary>
    public class ProtoFieldEditorSelector : DataTemplateSelector
    {
        public DataTemplate? ScalarTemplate { get; set; }
        public DataTemplate? BoolTemplate { get; set; }
        public DataTemplate? EnumTemplate { get; set; }
        public DataTemplate? MessageTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is ProtoFieldNode node)
            {
                // repeated 字段（无论 message 还是 scalar）本身没有单个可编辑值，值都在子项里
                if (node.IsRepeated || node.IsMessage) return MessageTemplate;
                if (node.IsEnum) return EnumTemplate;
                if (node.Descriptor?.FieldType == FieldType.Bool) return BoolTemplate;
                return ScalarTemplate;
            }
            return base.SelectTemplate(item, container);
        }
    }
}

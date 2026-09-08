using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RemoteDebugServer
{
    /// <summary>对象非 null 时返回 Visible，否则 Collapsed。</summary>
    public class NotNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>string "true"/"false" 与 bool 互转，用于 bool 字段的 CheckBox 绑定。</summary>
    public class BoolStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.Equals(value as string, "true", StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "true" : "false";
    }
}

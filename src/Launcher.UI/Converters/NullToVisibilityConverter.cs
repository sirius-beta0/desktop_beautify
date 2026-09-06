using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.UI.Converters;

/// <summary>绑定值为 null 或空字符串时返回 Collapsed，否则 Visible（用于「有值时显示」）。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

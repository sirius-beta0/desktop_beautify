using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.UI.Converters;

/// <summary>绑定值为 null 或空字符串时返回 Visible，否则 Collapsed（用于「无值时显示默认」）。</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

using System.Globalization;
using System.Windows.Data;

namespace Launcher.UI.Converters;

/// <summary>
/// 取字符串首字符（M2 占位图标的首字母头像用）。空串返回占位符。
/// </summary>
public sealed class FirstCharConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return "•";
        var ch = s.Trim()[0];
        return char.ToUpper(ch, culture).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

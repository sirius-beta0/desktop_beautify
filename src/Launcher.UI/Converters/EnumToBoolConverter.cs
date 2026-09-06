using System.Globalization;
using System.Windows.Data;

namespace Launcher.UI.Converters;

/// <summary>
/// 枚举 ↔ bool 双向转换器，用于 RadioButton 绑定枚举字段。
/// Convert：value.ToString() == parameter 时返回 true。
/// ConvertBack：勾选(true) 时按 parameter 解析回枚举；取消勾选返回 DoNothing，避免覆盖另一项。
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && parameter is string p && value.ToString() == p;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string p)
        {
            try { return Enum.Parse(targetType, p); }
            catch { }
        }
        return Binding.DoNothing;
    }
}

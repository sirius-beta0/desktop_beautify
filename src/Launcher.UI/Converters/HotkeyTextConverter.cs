using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using Launcher.Core.Settings;

namespace Launcher.UI.Converters;

/// <summary>
/// 把 [修饰键 int, 虚拟键码 int] 转成展示文本（如 "Ctrl+Alt+Space"）。
/// 值来自 AppSettings 的 HotkeyModifiers / HotkeyKey，配合 MultiBinding 使用。
/// </summary>
public sealed class HotkeyTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return "未设置";

        var modifiers = values[0] is int m ? m : 0;
        var vk = values[1] is int k ? k : 0;

        if (vk == 0) return "未设置";

        var parts = new List<string>(4);
        if ((modifiers & AppSettings.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & AppSettings.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & AppSettings.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & AppSettings.MOD_WIN) != 0) parts.Add("Win");

        try
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            parts.Add(key == Key.None ? $"#{vk}" : key.ToString());
        }
        catch
        {
            parts.Add($"#{vk}");
        }

        return string.Join("+", parts);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => new object[] { Binding.DoNothing, Binding.DoNothing };
}

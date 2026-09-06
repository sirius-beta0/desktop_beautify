using System.Windows;
using System.Windows.Input;
using Launcher.Core.Settings;
using Microsoft.Win32;

namespace Launcher.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _capturing;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = (AppSettings)Application.Current.Resources["AppSettings"]!;
        DataContext = _settings;
    }

    // ---- 中心按钮图标 ----

    private void OnPickIcon(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Dock 中心按钮图标",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.ico|所有文件|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        var crop = new IconCropWindow(dlg.FileName);
        if (crop.ShowDialog() == true && crop.OutputPath is not null)
        {
            _settings.DockCenterIconPath = crop.OutputPath;
        }
    }

    private void OnResetIcon(object sender, RoutedEventArgs e)
    {
        _settings.DockCenterIconPath = null;
    }

    // ---- 全局热键录制 ----

    private void OnRecordHotkey(object sender, RoutedEventArgs e)
    {
        _capturing = !_capturing;
        if (_capturing)
        {
            RecordBtn.Content = "按下组合键…";
            HotkeyHint.Text = "请按下快捷键组合（需含 Ctrl / Alt / Shift / Win 之一），再次点击按钮可取消录制。";
            Focus();   // 确保窗口接收键盘事件
        }
        else
        {
            RecordBtn.Content = "录制热键…";
            HotkeyHint.Text = "默认 Ctrl+Alt+Space。启用后即使本程序未激活，按下快捷键也会弹出搜索面板。";
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;

        // 仅按下修饰键时继续等待实际键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        var km = Keyboard.Modifiers;
        int mods = AppSettings.MOD_NONE;
        if (km.HasFlag(ModifierKeys.Control)) mods |= AppSettings.MOD_CONTROL;
        if (km.HasFlag(ModifierKeys.Alt)) mods |= AppSettings.MOD_ALT;
        if (km.HasFlag(ModifierKeys.Shift)) mods |= AppSettings.MOD_SHIFT;
        if (km.HasFlag(ModifierKeys.Windows)) mods |= AppSettings.MOD_WIN;

        if (mods == AppSettings.MOD_NONE)
        {
            HotkeyHint.Text = "未检测到修饰键，请同时按下 Ctrl / Alt / Shift / Win 之一再试。";
            _capturing = false;
            RecordBtn.Content = "录制热键…";
            return;
        }

        _settings.HotkeyModifiers = mods;
        _settings.HotkeyKey = KeyInterop.VirtualKeyFromKey(e.Key);
        _capturing = false;
        RecordBtn.Content = "录制热键…";
        HotkeyHint.Text = "已记录新快捷键，将在下次启用/更改后立即生效。";
    }

    // ---- 关于 / 关闭 ----

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var w = new AboutWindow { Owner = this };
        w.ShowDialog();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

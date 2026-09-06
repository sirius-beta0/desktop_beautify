using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Core.Settings;

/// <summary>面板从任务栏 / 热键唤起时的弹出位置。</summary>
public enum PanelPosition
{
    /// <summary>贴任务栏（默认左下角）。</summary>
    BottomLeft,
    /// <summary>屏幕底部水平居中。</summary>
    Center,
}

/// <summary>
/// 应用配置（M7-B 设置页）。单例，注入到 Application.Resources["AppSettings"] 供 XAML 绑定；
/// 字段变更通过 INotifyPropertyChanged 通知（Dock 图标/中心图标即时生效，开机自启/热键由 App 订阅联动）。
/// 落盘到 %LocalAppData%/DesktopBeautify/settings.json（仿 FavoriteStore 的容错读写）。
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    // RegisterHotKey 修饰键标志（与 user32 一致，避免 Core 引用 WPF 的 ModifierKeys）
    public const int MOD_NONE = 0;
    public const int MOD_ALT = 1;
    public const int MOD_CONTROL = 2;
    public const int MOD_SHIFT = 4;
    public const int MOD_WIN = 8;

    private bool _hotkeyEnabled;
    private int _hotkeyModifiers = MOD_CONTROL | MOD_ALT;   // 默认 Ctrl+Alt
    private int _hotkeyKey = 0x20;                          // 默认 VK_SPACE
    private string? _dockCenterIconPath;
    private bool _runAtStartup;
    private double _dockIconSize = 32;
    private bool _showDock = true;
    private PanelPosition _panelPosition = PanelPosition.BottomLeft;

    public bool HotkeyEnabled
    {
        get => _hotkeyEnabled;
        set => Set(ref _hotkeyEnabled, value);
    }

    public int HotkeyModifiers
    {
        get => _hotkeyModifiers;
        set => Set(ref _hotkeyModifiers, value);
    }

    public int HotkeyKey
    {
        get => _hotkeyKey;
        set => Set(ref _hotkeyKey, value);
    }

    /// <summary>Dock 中心按钮自定义图标路径；null/空 = 使用默认 X 图标。</summary>
    public string? DockCenterIconPath
    {
        get => _dockCenterIconPath;
        set => Set(ref _dockCenterIconPath, value);
    }

    public bool RunAtStartup
    {
        get => _runAtStartup;
        set => Set(ref _runAtStartup, value);
    }

    /// <summary>Dock 图标尺寸（逻辑像素），范围 24–56。</summary>
    public double DockIconSize
    {
        get => _dockIconSize;
        set => Set(ref _dockIconSize, Clamp(value, 24, 56));
    }

    /// <summary>是否显示 Dock 栏（默认 true）。</summary>
    public bool ShowDock
    {
        get => _showDock;
        set => Set(ref _showDock, value);
    }

    /// <summary>面板从任务栏 / 热键唤起时的弹出位置（默认 BottomLeft 左下角）。</summary>
    public PanelPosition PanelPosition
    {
        get => _panelPosition;
        set => Set(ref _panelPosition, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Save();   // 改即落盘，用户无需手动保存；Load 阶段用 setter 写入同值属幂等，无副作用
    }

    private static double Clamp(double v, double min, double max)
        => v < min ? min : v > max ? max : v;

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopBeautify", "settings.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var doc = JsonSerializer.Deserialize<Doc>(File.ReadAllText(StorePath));
            if (doc is null) return;
            if (doc.HotkeyEnabled is bool h) HotkeyEnabled = h;
            if (doc.HotkeyModifiers is int m) HotkeyModifiers = m;
            if (doc.HotkeyKey is int k) HotkeyKey = k;
            DockCenterIconPath = doc.DockCenterIconPath;
            if (doc.RunAtStartup is bool r) RunAtStartup = r;
            if (doc.DockIconSize is double s) DockIconSize = s;
            if (doc.ShowDock is bool sd) ShowDock = sd;
            if (doc.PanelPosition is PanelPosition p) PanelPosition = p;
        }
        catch
        {
            // 文件损坏则用默认值兜底
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (dir is not null) Directory.CreateDirectory(dir);
            var doc = new Doc
            {
                HotkeyEnabled = HotkeyEnabled,
                HotkeyModifiers = HotkeyModifiers,
                HotkeyKey = HotkeyKey,
                DockCenterIconPath = DockCenterIconPath,
                RunAtStartup = RunAtStartup,
                DockIconSize = DockIconSize,
                ShowDock = ShowDock,
                PanelPosition = PanelPosition,
            };
            var json = JsonSerializer.Serialize(doc,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // 忽略写失败（下次变更再试）
        }
    }

    private sealed class Doc
    {
        public bool? HotkeyEnabled { get; set; }
        public int? HotkeyModifiers { get; set; }
        public int? HotkeyKey { get; set; }
        public string? DockCenterIconPath { get; set; }
        public bool? RunAtStartup { get; set; }
        public double? DockIconSize { get; set; }
        public bool? ShowDock { get; set; }
        public PanelPosition? PanelPosition { get; set; }
    }
}

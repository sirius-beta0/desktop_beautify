using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Shell;
using Microsoft.Win32;
using Launcher.Core.Favorites;
using Launcher.Core.Indexing;
using Launcher.Core.Platform;
using Launcher.Core.Settings;
using Launcher.UI;
using Application = System.Windows.Application;

namespace Launcher.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private InstancePipeServer? _pipeServer;
    private PanelWindow? _panel;
    private DockWindow? _dock;
    private NotifyIcon? _tray;
    private PanelViewModel? _vm;
    private AppIndexer? _indexer;
    private AppSettings? _settings;

    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        AttachCrashLogging();
        base.OnStartup(e);

        // 单实例
        _mutex = new Mutex(initiallyOwned: true,
                           name: "DesktopBeautify.Launcher.SingleInstance",
                           createdNew: out var createdNew);
        if (!createdNew)
        {
            // 第二个实例：把请求转发给首实例（任务栏 JumpList 的「设置/关于」即走此路径）
            var msg = e.Args.Contains("--settings") ? InstancePipe.SettingsMessage
                     : e.Args.Contains("--about") ? InstancePipe.AboutMessage
                     : InstancePipe.ToggleMessage;
            InstancePipe.TrySend(msg);
            Shutdown();
            return;
        }

        // 配置：加载并注入全局资源，供 Dock / 设置页绑定
        _settings = new AppSettings();
        _settings.Load();
        Resources["AppSettings"] = _settings;
        _settings.PropertyChanged += OnSettingsChanged;

        _indexer = new AppIndexer();
        _vm    = new PanelViewModel(new FavoriteStore());
        _panel = new PanelWindow { ViewModel = _vm };
        _panel.PanelAnchor = _settings.PanelPosition;   // 面板弹出位置（左下角 / 居中 / Dock 上方）
        _tray  = BuildTray();
        _pipeServer = new InstancePipeServer(OnPipeMessage);

        // Dock 栏常驻桌面：作为应用搜索面板的主入口；面板默认隐藏，由 Dock 中心按钮唤起
        _dock = new DockWindow { ViewModel = _vm, Panel = _panel };
        _panel.Dock = _dock;                        // 供「Dock 上方」弹出位置计算锚点
        _dock.HotkeyPressed = RequestTogglePanel;   // 全局热键 → 切换面板
        _dock.Show();
        if (!_settings.ShowDock) _dock.Hide();       // 设置关 Dock：初始即隐藏（热键 / 托盘仍可唤起面板）

        // 启动参数：首实例若带 --settings/--about，初始化完成后直接打开对应窗口
        if (e.Args.Contains("--settings")) ShowSettingsWindow();
        else if (e.Args.Contains("--about")) ShowAboutWindow();

        // 先尝试缓存秒开（冷启动不转圈），再后台全量扫描刷新
        var cached = _indexer.LoadCache();
        if (cached is not null)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.SetApps(cached);
                _panel?.RefreshEmptyState();
            });
        }

        _ = ScanAppsAsync();

        // 按当前配置注册全局热键（Dock 句柄已在 Show 后就绪）
        _dock.ApplyHotkeySettings(_settings);
    }

    private async Task ScanAppsAsync()
    {
        if (_vm is null || _indexer is null) return;
        try
        {
            var apps = await _indexer.BuildAsync();
            // 切回 UI 线程写集合（ObservableCollection 非线程安全）
            await Dispatcher.InvokeAsync(() =>
            {
                _vm.SetApps(apps);
                _panel?.RefreshEmptyState();
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() => _vm.IsIndexing = false);
        }
    }

    private void TogglePanel()
    {
        if (_panel is null) return;
        Dispatcher.Invoke(() =>
        {
            var info = TaskbarInfoProvider.GetPrimaryTaskbar();
            if (info is null) return;
            _panel.Toggle(info);
        });
    }

    /// <summary>全局热键 / 任务栏 JumpList 切换面板入口（公用于 Dock 热键回调）。</summary>
    public void RequestTogglePanel() => TogglePanel();

    // ---- 单实例 pipe 消息分发 ----

    private void OnPipeMessage(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            if (msg == InstancePipe.SettingsMessage) ShowSettingsWindow();
            else if (msg == InstancePipe.AboutMessage) ShowAboutWindow();
            else TogglePanel();
        });
    }

    // ---- 设置变更联动 ----

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.RunAtStartup) && _settings is not null)
            ApplyRunAtStartup(_settings.RunAtStartup);
        else if ((e.PropertyName is nameof(AppSettings.HotkeyEnabled)
                  or nameof(AppSettings.HotkeyModifiers)
                  or nameof(AppSettings.HotkeyKey))
                 && _dock is not null && _settings is not null)
            _dock.ApplyHotkeySettings(_settings);
        else if (e.PropertyName == nameof(AppSettings.PanelPosition)
                 && _panel is not null && _settings is not null)
            _panel.PanelAnchor = _settings.PanelPosition;   // 面板弹出位置即时生效
        else if (e.PropertyName == nameof(AppSettings.ShowDock)
                 && _dock is not null && _settings is not null)
        {
            // 显示 / 隐藏 Dock 栏（Dock 的 Closing 已改为 Hide 保活，此处可直接切换可见性；
            // 即使隐藏，Dock 句柄仍在，全局热键与托盘仍可唤起面板）
            if (_settings.ShowDock) _dock.Show();
            else _dock.Hide();
        }
    }

    private static void ApplyRunAtStartup(bool enable)
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (enable && exePath is not null)
                key.SetValue("DesktopBeautify.Launcher", exePath);
            else
                key.DeleteValue("DesktopBeautify.Launcher", false);
        }
        catch
        {
            // 注册表写入失败（极少）忽略，下次再试
        }
    }

    // ---- 设置 / 关于窗口（单例）----

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (_settingsWindow.IsVisible) _settingsWindow.Activate();
        else _settingsWindow.Show();
    }

    private void ShowAboutWindow()
    {
        if (_aboutWindow is null)
        {
            _aboutWindow = new AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        }
        if (_aboutWindow.IsVisible) _aboutWindow.Activate();
        else _aboutWindow.Show();
    }

    // ---- 托盘 + JumpList ----

    private NotifyIcon BuildTray()
    {
        // 单文件发布下 Assembly.Location 为空，故用进程主模块路径取 exe（单文件安全）
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        var icon = (exePath is not null ? Icon.ExtractAssociatedIcon(exePath) : null) ?? SystemIcons.Application;

        var tray = new NotifyIcon
        {
            Text = "DesktopBeautify Launcher",
            Icon = icon,
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        var toggleItem = new ToolStripMenuItem("显示 / 隐藏面板");
        toggleItem.Click += (_, _) => TogglePanel();
        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => ShowSettingsWindow();
        var aboutItem = new ToolStripMenuItem("关于");
        aboutItem.Click += (_, _) => ShowAboutWindow();
        var refreshItem = new ToolStripMenuItem("刷新应用列表");
        refreshItem.Click += async (_, _) => await ScanAppsAsync();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(toggleItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => TogglePanel();

        BuildJumpList(exePath);
        return tray;
    }

    private static void BuildJumpList(string? exePath)
    {
        var jl = new JumpList();
        jl.ShowRecentCategory = false;
        jl.ShowFrequentCategory = false;
        if (exePath is not null)
        {
            jl.JumpItems.Add(new JumpTask
            {
                Title = "设置",
                Description = "打开设置窗口",
                ApplicationPath = exePath,
                Arguments = "--settings",
                IconResourcePath = exePath,
            });
            jl.JumpItems.Add(new JumpTask
            {
                Title = "关于",
                Description = "关于 DesktopBeautify Launcher",
                ApplicationPath = exePath,
                Arguments = "--about",
                IconResourcePath = exePath,
            });
        }
        JumpList.SetJumpList(Current, jl);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _pipeServer?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ---- 全局未处理异常捕获：把真实堆栈落到 crash.log，便于无调试环境下定位崩溃 ----
    // WER 报 0xe0434352 只能说明是 .NET 托管异常，拿不到托管栈；这里兜底记录。

    private static string CrashLogPath =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
                ?? ".",
            "crash.log");

    private void AttachCrashLogging()
    {
        // UI 线程异常：记录并保活，避免单次异常直接退出进程
        Dispatcher.UnhandledException += (_, e) =>
        {
            WriteCrash(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
    }

    private static void WriteCrash(Exception? ex, string where)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {where}");
            sb.AppendLine(ex?.ToString() ?? "(null exception)");
            sb.AppendLine(new string('-', 60));
            System.IO.File.AppendAllText(CrashLogPath, sb.ToString());
        }
        catch
        {
            // 日志记录本身失败则忽略，不要因日志二次崩溃
        }
    }
}

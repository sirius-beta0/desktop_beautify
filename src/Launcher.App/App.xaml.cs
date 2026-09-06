using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Launcher.Core.Favorites;
using Launcher.Core.Indexing;
using Launcher.Core.Platform;
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
            // 让首实例切换面板
            InstancePipe.TrySend(InstancePipe.ToggleMessage);
            Shutdown();
            return;
        }

        _indexer = new AppIndexer();
        _vm    = new PanelViewModel(new FavoriteStore());
        _panel = new PanelWindow { ViewModel = _vm };
        _tray  = BuildTray();
        _pipeServer = new InstancePipeServer(TogglePanel);

        // Dock 栏常驻桌面：作为应用搜索面板的主入口；面板默认隐藏，由 Dock 中心按钮唤起
        _dock = new DockWindow { ViewModel = _vm, Panel = _panel };
        _dock.Show();

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

    private NotifyIcon BuildTray()
    {
        // 单文件发布下 Assembly.Location 为空，故用进程主模块路径取 exe（单文件安全）
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
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
        var refreshItem = new ToolStripMenuItem("刷新应用列表");
        refreshItem.Click += async (_, _) => await ScanAppsAsync();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(toggleItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => TogglePanel();

        return tray;
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
            System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName)
                ?? ".",
            "crash.log");

    private void AttachCrashLogging()
    {
        // UI 线程异常：记录并保活，避免单次异常直接退出进程
        DispatcherUnhandledException += (_, e) =>
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

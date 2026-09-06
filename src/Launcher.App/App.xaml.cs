using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Launcher.Core.Platform;
using Launcher.UI;
using Application = System.Windows.Application;

namespace Launcher.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private InstancePipeServer? _pipeServer;
    private PanelWindow? _panel;
    private NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
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

        _panel  = new PanelWindow();
        _tray   = BuildTray();
        _pipeServer = new InstancePipeServer(TogglePanel);

        // 首启动直接展示一次面板；后续通过 IPC 或托盘切换
        TogglePanel();
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
        var exe = Assembly.GetExecutingAssembly().Location;
        var icon = Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application;

        var tray = new NotifyIcon
        {
            Text = "DesktopBeautify Launcher",
            Icon = icon,
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        var toggleItem = new ToolStripMenuItem("显示 / 隐藏面板");
        toggleItem.Click += (_, _) => TogglePanel();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(toggleItem);
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
}
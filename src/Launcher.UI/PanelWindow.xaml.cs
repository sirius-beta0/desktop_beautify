using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Launcher.Core.Platform;

namespace Launcher.UI;

public partial class PanelWindow : Window
{
    private bool _everActivated;

    public PanelWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HidePanel();
        };
        Deactivated += OnDeactivated;
        Activated += (_, _) => _everActivated = true;
        Loaded += (_, _) => SearchBox.Focus();
        UpdatePlaceholder();
    }

    private void OnSearchStateChanged(object sender, RoutedEventArgs e) => UpdatePlaceholder();
    private void OnSearchStateChanged(object sender, TextChangedEventArgs e) => UpdatePlaceholder();

    private void UpdatePlaceholder()
    {
        var show = string.IsNullOrEmpty(SearchBox.Text) && !SearchBox.IsKeyboardFocused;
        SearchPlaceholder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 根据任务栏信息贴边放置面板。后续每次显示前都应调用，因为任务栏位置可能改变。
    /// </summary>
    public void PlaceAt(TaskbarInfo info)
    {
        double scale = info.ScaleFactor;

        double TbLeft   = info.Bounds.Left   / scale;
        double TbTop    = info.Bounds.Top    / scale;
        double TbRight  = info.Bounds.Right  / scale;
        double TbBottom = info.Bounds.Bottom / scale;

        double WorkLeft   = info.MonitorWorkArea.Left   / scale;
        double WorkTop    = info.MonitorWorkArea.Top    / scale;
        double WorkRight  = info.MonitorWorkArea.Right  / scale;
        double WorkBottom = info.MonitorWorkArea.Bottom / scale;

        double w = Width;
        double h = Height;
        double margin = 4;

        double left = TbLeft + 8;
        double top  = TbTop  - h;       // 默认贴任务栏上沿

        switch (info.Edge)
        {
            case TaskbarEdge.Bottom:
                left = TbLeft + 8;
                top  = TbTop - h;
                break;
            case TaskbarEdge.Top:
                left = TbLeft + 8;
                top  = TbBottom;
                break;
            case TaskbarEdge.Left:
                left = TbRight;
                top  = TbTop + 8;
                break;
            case TaskbarEdge.Right:
                left = TbLeft - w;
                top  = TbTop + 8;
                break;
        }

        // 限制在工作区内，避免多屏或长任务栏场景溢出
        if (left + w > WorkRight - margin) left = WorkRight - w - margin;
        if (left < WorkLeft + margin)       left = WorkLeft + margin;
        if (top  + h > WorkBottom - margin) top  = WorkBottom - h - margin;
        if (top  < WorkTop + margin)        top  = WorkTop + margin;

        Left = left;
        Top  = top;
    }

    /// <summary>
    /// 唤起 / 隐藏面板的入口。供单实例 IPC 与托盘菜单复用。
    /// </summary>
    public void Toggle(TaskbarInfo info)
    {
        if (IsVisible)
        {
            HidePanel();
            return;
        }
        PlaceAt(info);
        Show();
        Activate();
        _everActivated = false;   // 重置，下次 Deactivated 由本次激活算起
    }

    public void HidePanel() => Hide();

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_everActivated) return;
        HidePanel();
    }
}
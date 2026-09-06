using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Launcher.Core;
using Launcher.Core.Indexing;
using Launcher.Core.Platform;

namespace Launcher.UI;

public partial class PanelWindow : Window
{
    private PanelViewModel? _vm;
    private DateTime _shownAt = DateTime.MinValue;

    public PanelViewModel ViewModel
    {
        get => _vm!;
        set
        {
            _vm = value;
            DataContext = value;
            RefreshEmptyState();
        }
    }

    public PanelWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HidePanel();
        };
        Deactivated += OnDeactivated;
        Loaded += (_, _) => SearchBox.Focus();
        UpdatePlaceholder();
    }

    private void OnSearchStateChanged(object sender, RoutedEventArgs e) => UpdatePlaceholder();
    private void OnSearchStateChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        RefreshEmptyState();
    }

    private void UpdatePlaceholder()
    {
        var show = string.IsNullOrEmpty(SearchBox.Text) && !SearchBox.IsKeyboardFocused;
        SearchPlaceholder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 根据当前列表 / 搜索状态切换空提示与网格的可见性。
    /// </summary>
    public void RefreshEmptyState()
    {
        if (_vm is null) return;

        var hasItems = _vm.AppsView.Cast<AppEntry>().Any();
        AppGrid.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

        EmptyHint.Text = _vm.IsIndexing
            ? "正在索引已安装应用…"
            : !string.IsNullOrWhiteSpace(_vm.SearchText)
                ? "未找到匹配的应用"
                : "未索引到应用（请检查开始菜单目录）";
    }

    /// <summary>网格项单击即启动。M5 再加收藏与回车选择。</summary>
    private void OnAppLaunch(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AppEntry app) return;
        AppLauncher.Launch(app);
        HidePanel();
    }

    /// <summary>
    /// 单个网格项加载时异步提取 256px 真图标。提取成功则覆盖占位并隐藏首字母
    /// （否则透明图标会透出下方的占位字母与底色）；提取失败保留首字母占位。
    /// </summary>
    private async void OnTileLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image img || img.DataContext is not AppEntry app) return;
        var icon = await IconExtractor.GetAsync(app.Id, app.TargetPath ?? app.IconPath ?? "").ConfigureAwait(true);
        if (img.DataContext != app) return;
        if (icon is null) return;
        img.Source = icon;
        var placeholder = FindChildBorder((Grid)img.Parent, "Placeholder");
        if (placeholder is not null) placeholder.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 在视觉树中按 Tag 查找第一个匹配的 Border（用于隐藏占位层）。
    /// </summary>
    private static Border? FindChildBorder(DependencyObject parent, object tag)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border b && Equals(b.Tag, tag)) return b;
            var nested = FindChildBorder(child, tag);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>
    /// 根据任务栏信息贴边放置面板。每次显示前都应调用，因为任务栏位置可能改变。
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
        _shownAt = DateTime.Now;
        Show();
        Activate();
        SearchBox.Focus();
    }

    public void HidePanel() => Hide();

    /// <summary>
    /// 面板失焦即隐藏，但显示后的极短时间内忽略 Deactivated，
    /// 避免 Show/Activate 过程中系统瞬间切走焦点导致误关闭。
    /// </summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (DateTime.Now - _shownAt < TimeSpan.FromMilliseconds(200)) return;
        HidePanel();
    }
}

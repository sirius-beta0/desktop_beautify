using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.Core;
using Launcher.Core.Dock;
using Launcher.Core.Indexing;

namespace Launcher.UI;

/// <summary>
/// 桌面 Dock 栏（M6）：透明置顶长条，常驻桌面，自由拖动并记忆位置。
/// 展示收藏应用图标（最多 16 个，越靠前越靠近中心按钮），点击启动；
/// 中心按钮点击切换应用搜索面板（从 Dock 上方弹出）。
/// </summary>
public partial class DockWindow : Window
{
    private PanelViewModel? _vm;
    private readonly DockPositionStore _positionStore = new();
    private bool _dragging;
    private Point _dragStart;

    /// <summary>
    /// 中心按钮即将切换面板的待定标记。在按钮 PreviewMouseDown（早于面板失焦）置位，
    /// 供 PanelWindow.OnDeactivated 判断是否“因点击中心按钮而失焦”，从而跳过自动隐藏、
    /// 把开合完全交给中心按钮。每次切换结束后清零，避免误伤真实的外点收起。
    /// </summary>
    internal static bool CenterTogglePending;

    /// <summary>由 App 注入的搜索面板，用于中心按钮唤起。</summary>
    public PanelWindow? Panel { get; set; }

    public PanelViewModel ViewModel
    {
        get => _vm!;
        set { _vm = value; DataContext = value; }
    }

    public DockWindow()
    {
        InitializeComponent();
        // 常驻：关闭改为隐藏，进程仍由托盘管理（避免误关整个应用）
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_positionStore.Left is double lx && _positionStore.Top is double ty)
        {
            Left = lx;
            Top = ty;
        }
        else
        {
            // 首次默认主屏底部居中
            var wa = SystemParameters.WorkArea;
            Left = (wa.Left + wa.Right) / 2 - ActualWidth / 2;
            Top = wa.Bottom - ActualHeight - 8;
        }
    }

    // ---- 中心按钮：切换面板（从 Dock 上方弹出）----

    private void OnCenterButtonClick(object sender, RoutedEventArgs e)
    {
        if (Panel is null) return;
        CenterTogglePending = false; // 切换结束，清除待定标记（防残留导致外点不收起）
        if (Panel.IsVisible) { Panel.HidePanel(); return; }
        ShowPanelAbove();
    }

    private void ShowPanelAbove()
    {
        if (Panel is null) return;
        var src = PresentationSource.FromVisual(this);
        var dpm = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        // 以中心按钮“顶部中心”为锚点（而非中心点），使面板底边悬于按钮正上方、无重叠
        var btnAnchorInWindow = CenterButton.TransformToVisual(this)
            .Transform(new Point(CenterButton.ActualWidth / 2, 0));
        var screenPhys = PointToScreen(btnAnchorInWindow);
        var logicalX = screenPhys.X / dpm.M11;
        var logicalY = screenPhys.Y / dpm.M22;
        Panel.PositionAbove(logicalX, logicalY);
        Panel.Show();
        Panel.Activate();
    }

    // ---- 拖动：空白处（非图标/非中心按钮）按下并移动超过阈值即拖动整个窗体 ----

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (HitTag(e.OriginalSource as DependencyObject, "DockIcon")) return;       // 图标：交给启动
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            CenterTogglePending = true; // 中心按钮即将切换，标记待定（Panel 失焦时据此跳过自动隐藏）
            return;                      // 按钮：交给切换
        }
        _dragging = true;
        _dragStart = e.GetPosition(this);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y) > 3)
        {
            _dragging = false;
            DragMove();
            SavePosition();
        }
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => _dragging = false;

    private void SavePosition()
    {
        _positionStore.Left = Left;
        _positionStore.Top = Top;
        _positionStore.Save();
    }

    // ---- 图标：悬浮放大、点击启动、异步图标提取 ----

    // 悬浮放大改由 DockIconTemplate 的 XAML IsMouseOver 触发器实现（纯属性系统，
    // 不依赖 DataTemplate 内的路由事件处理器解析，规避此前 MouseEnter 静默失效的问题）。

    private void OnDockIconLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image img || img.DataContext is not AppEntry app) return;
        if (app.IsPlaceholder) return;
        var iconKey = app.AppUserModelId is not null
            ? "shell:appsFolder\\" + app.AppUserModelId
            : (app.TargetPath ?? app.IconPath ?? "");
        _ = LoadIconAsync(img, app, iconKey);
    }

    private async Task LoadIconAsync(Image img, AppEntry app, string iconKey)
    {
        var icon = await IconExtractor.GetAsync(app.Id, iconKey).ConfigureAwait(true);
        if (img.DataContext != app) return;
        if (icon is null) return;
        img.Source = icon;
        if (img.Parent is DependencyObject parent)
        {
            var ph = FindChild<Border>(parent, "DockPh");
            if (ph is not null) ph.Visibility = Visibility.Collapsed;
        }
    }

    private void OnDockAppLaunch(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AppEntry app) return;
        if (app.IsPlaceholder) return;
        AppLauncher.Launch(app);
    }

    // ---- 视觉树辅助 ----

    // 中心按钮自身 PreviewMouseDown：必置位切换待定标记（比窗口级隧道+FindAncestor 更可靠），
    // 供 PanelWindow.OnDeactivated 判断是否因点中心按钮而失焦，从而跳过自动隐藏。
    private void OnCenterButtonPreviewDown(object sender, MouseButtonEventArgs e)
        => CenterTogglePending = true;

    private static bool HitTag(DependencyObject? o, object tag)
    {
        while (o is not null)
        {
            if (o is FrameworkElement fe && fe.Tag is not null && fe.Tag.Equals(tag)) return true;
            o = VisualTreeHelper.GetParent(o);
        }
        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? o) where T : DependencyObject
    {
        while (o is not null)
        {
            if (o is T t) return t;
            o = VisualTreeHelper.GetParent(o);
        }
        return null;
    }

    private static T? FindChild<T>(DependencyObject? o, object? tag) where T : FrameworkElement
    {
        if (o is null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            if (c is T t && (tag is null || Equals(t.Tag, tag))) return t;
            var nested = FindChild<T>(c, tag);
            if (nested is not null) return nested;
        }
        return null;
    }
}

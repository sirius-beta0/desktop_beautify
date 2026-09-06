using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Launcher.Core;
using Launcher.Core.Dock;
using Launcher.Core.Indexing;

namespace Launcher.UI;

/// <summary>
/// 桌面 Dock 栏（M6）：透明常驻长条，与桌面图标同级（非 Topmost，不挡应用窗口），自由拖动并记忆位置。
/// 展示收藏应用图标（最多 16 个，越靠前越靠近中心按钮），点击启动；
/// 中心按钮点击切换应用搜索面板（从 Dock 上方弹出）。
/// </summary>
public partial class DockWindow : Window
{
    private PanelViewModel? _vm;
    private readonly DockPositionStore _positionStore = new();
    private bool _dragging;
    private Point _dragStart;

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
        // 窗口句柄创建后设置扩展样式：WS_EX_TOOLWINDOW 让 Dock 不出现在
        // Alt+Tab / 任务栏切换列表，更像“桌面小组件”（不影响点击穿透与层级）。
        SourceInitialized += OnSourceInitialized;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
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

    // 中心按钮抬起（PreviewMouseUp，隧道事件，Button 内部把 MouseLeftButtonUp 标 Handled 也不影响它必定触发，
    // 且能覆盖“按下后拖离再松开”的手势）：切换面板。鼠标已释放、无按键捕获冲突，ShowPanelAbove 的
    // Activate() 能稳定保持激活，不会像「按下时激活」那样闪一下就被系统重新激活回 Dock 而收起。
    // 自动收起的竞态不在此处理：PanelWindow.OnDeactivated 用“前台窗口是否本进程”判定，
    // 点中心按钮时前台变 Dock（同进程）不收起，由本方法决定开合，彻底消除重开/闪烁。
    private void OnCenterButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Panel is null) return;
        if (Panel.IsVisible) Panel.HidePanel();
        else ShowPanelAbove();
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
        Panel.PositionAbove(logicalX, logicalY);  // 先用 Measure 估算高度定位
        Panel.Show();
        Panel.UpdateLayout();                     // 强制布局：ActualHeight 此刻为真实渲染高度
        Panel.PositionAbove(logicalX, logicalY);  // 用真实高度二次校正（同一步内，无闪烁）
        Panel.Activate();
    }

    // ---- 拖动：空白处（非图标/非中心按钮）按下并移动超过阈值即拖动整个窗体 ----

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (HitTag(e.OriginalSource as DependencyObject, "DockIcon")) return;       // 图标：交给启动
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;                      // 中心按钮：不触发拖动，切换交给 OnCenterButtonPreviewDown
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
        if (app.IsPlaceholder || app.IsDockAdd) return;
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
        if (app.IsDockAdd) { ShowPanelAbove(); return; }   // M7-C：透明 + 号占位 → 打开搜索面板
        if (app.IsPlaceholder) return;
        AppLauncher.Launch(app);
    }

    // ---- 视觉树辅助 ----

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

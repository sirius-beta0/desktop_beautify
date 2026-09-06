using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Launcher.Core;
using Launcher.Core.Indexing;
using Launcher.Core.Platform;
using Launcher.Core.Settings;

namespace Launcher.UI;

public partial class PanelWindow : Window
{
    private PanelViewModel? _vm;
    private DateTime _shownAt = DateTime.MinValue;

    // 收藏区拖拽状态（占位卡模型）
    private AppEntry? _dragApp;
    private int _dragOriginalIndex = -1;     // 被拖动卡在收藏区的原始索引
    private readonly AppEntry _placeholder = new() { Id = "__drag_placeholder__", Name = "", IsPlaceholder = true };

    // 当前锚定方式：内容高度变化（搜索筛选 / 清空搜索）时据此重新锚定，保持底边贴住 Dock / 任务栏，
    // 避免面板变高后向下溢出盖住 Dock。
    private enum AnchorMode { None, Dock, Taskbar, Center }
    private AnchorMode _anchorMode = AnchorMode.None;
    private double _dockAnchorX, _dockAnchorY;   // Dock 中心按钮顶部中心（WPF 逻辑坐标）
    private TaskbarInfo? _taskbarInfo;
    private bool _reanchoring;
    private Point _favoriteDragStartPoint;   // 按下时的屏幕坐标
    private Point _grabOffset;               // 光标相对卡片左上角的偏移（DragCanvas 本地坐标）
    private Border? _dragGhost;              // 拖拽时跟随光标的卡片残影（选中态样式、缩小 40%）
    private bool _longPressArmed;
    private bool _favoriteDragInProgress;
    private bool _justDragged;
    private readonly DispatcherTimer _pressTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };

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

    /// <summary>面板从任务栏 / 热键唤起时的弹出位置（由设置注入；Dock 中心按钮唤起始终走 Dock 锚定）。</summary>
    public PanelPosition PanelAnchor { get; set; } = PanelPosition.BottomLeft;

    public PanelWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_favoriteDragInProgress) CancelPress();
                HidePanel();
            }
        };
        Deactivated += OnDeactivated;
        Loaded += (_, _) => SearchBox.Focus();
        SizeChanged += OnSizeChanged;   // 窗口尺寸变化兜底重锚定（固定高度下仅首次布局触发；搜索筛选在 ScrollViewer 内滚动，不触发本事件，面板不跳动）
        _pressTimer.Tick += OnLongPressTick;
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

        var favoritesVisible = _vm.ShowFavorites && _vm.Favorites.Any();
        var mainHasItems = _vm.Main.Any();
        var hasItems = favoritesVisible || mainHasItems;
        MainGrid.Visibility = mainHasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

        EmptyHint.Text = _vm.IsIndexing
            ? "正在索引已安装应用…"
            : !string.IsNullOrWhiteSpace(_vm.SearchText)
                ? "未找到匹配的应用"
                : "未索引到应用（请检查开始菜单目录）";
    }

    /// <summary>网格项单击即启动。拖拽刚结束时跳过，避免误启动。</summary>
    private void OnAppLaunch(object sender, MouseButtonEventArgs e)
    {
        if (_favoriteDragInProgress || _justDragged)
        {
            _favoriteDragInProgress = false;
            _justDragged = false;
            ResetAllShrink();
            return;
        }
        if (sender is not FrameworkElement fe || fe.DataContext is not AppEntry app) return;
        if (app.IsPlaceholder) return; // 占位卡不可启动
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
        if (app.IsPlaceholder) return; // 占位卡不提取图标
        // UWP 条目用 shell:appsFolder\<AUMID> 提取图标；Win32 用 TargetPath / IconPath
        var iconKey = app.AppUserModelId is not null
            ? "shell:appsFolder\\" + app.AppUserModelId
            : (app.TargetPath ?? app.IconPath ?? "");
        var icon = await IconExtractor.GetAsync(app.Id, iconKey).ConfigureAwait(true);
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

    // ---- 收藏：右键菜单 / 拖拽排序 ----

    /// <summary>右键菜单打开时，按当前项是否为收藏来设置菜单文案，并仅在失效收藏时显示「移除」。</summary>
    private void OnCardContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        if (menu.PlacementTarget is not FrameworkElement fe || fe.DataContext is not AppEntry app || _vm is null) return;

        if (menu.Items[0] is MenuItem favItem)
            favItem.Header = _vm.IsFavorite(app) ? "取消收藏" : "收藏";

        if (menu.Items[1] is MenuItem removeItem)
            removeItem.Visibility = (!app.IsValid && _vm.IsFavorite(app))
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>右键菜单「收藏 / 取消收藏 / 移除失效收藏」统一走切换。</summary>
    private void OnToggleFavoriteMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Parent is not ContextMenu cm) return;
        if (cm.PlacementTarget is not FrameworkElement fe || fe.DataContext is not AppEntry app) return;
        _vm?.ToggleFavorite(app);
        RefreshEmptyState();
    }

    private void OnFavoriteDragStart(object sender, MouseButtonEventArgs e)
    {
        _favoriteDragInProgress = false;
        _justDragged = false;
        _longPressArmed = false;
        _dragApp = null;
        if (_vm is null) return;
        // 事件挂在 ItemsControl 上，sender.DataContext 是 ViewModel 而非卡片，
        // 必须顺着鼠标原始落点往视觉树里找真正的 AppEntry 卡片。
        var app = FindAppEntryFromSource(e.OriginalSource as DependencyObject);
        if (app is null) return;
        if (_vm.Favorites.IndexOf(app) < 0) return;
        _dragApp = app;
        _favoriteDragStartPoint = e.GetPosition(null);
        // 启动长按计时：按住不移动 ~220ms 即进入拖拽态（卡片缩小作为反馈）
        _pressTimer.Stop();
        _pressTimer.Start();
    }

    /// <summary>从鼠标落点向上回溯视觉树，找到 DataContext 为 AppEntry 的卡片。</summary>
    private static AppEntry? FindAppEntryFromSource(DependencyObject? o)
    {
        while (o is not null)
        {
            if (o is FrameworkElement fe && fe.DataContext is AppEntry app) return app;
            o = VisualTreeHelper.GetParent(o);
        }
        return null;
    }

    /// <summary>长按到点：按钮仍按住则进入拖拽态，生成跟随光标的深色卡片残影。</summary>
    private void OnLongPressTick(object? sender, EventArgs e)
    {
        _pressTimer.Stop();
        if (_dragApp is null) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) return;
        _longPressArmed = true;
        BeginDrag();
    }

    private void OnFavoriteDragMove(object sender, MouseEventArgs e)
    {
        if (_dragApp is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelPress(); return; }
        var screen = e.GetPosition(null);
        if (!_favoriteDragInProgress)
        {
            var diff = screen - _favoriteDragStartPoint;
            // 移动超过阈值，或已长按进入拖拽态 → 开始拖拽
            if (Math.Abs(diff.X) <= 5 && Math.Abs(diff.Y) <= 5 && !_longPressArmed) return;
            BeginDrag();
        }
        if (_favoriteDragInProgress)
        {
            MoveGhostTo(screen);
            UpdateDragReorder(e);
        }
    }

    /// <summary>
    /// 拖拽过程中：找光标下的真实卡片（跳过占位卡），与其做两两交换。
    /// 不再要求 50% 重叠、也不按中心判断前后——光标一旦进入另一张卡片范围即交换，
    /// 占位空卡移到该卡原位、该卡补到拖动卡原位；离开卡片则保持上一状态（循环直到松手）。
    /// </summary>
    private void UpdateDragReorder(MouseEventArgs e)
    {
        if (_vm is null || _dragApp is null) return;

        var pt = e.GetPosition(FavoritesGrid);
        var hit = FavoritesGrid.InputHitTest(pt) as DependencyObject;
        AppEntry? targetApp = null;
        while (hit is not null)
        {
            if (hit is FrameworkElement fe && fe.DataContext is AppEntry a && !a.IsPlaceholder) { targetApp = a; break; }
            hit = VisualTreeHelper.GetParent(hit);
        }
        if (targetApp is not null)
            _vm.SetFavoriteDragTarget(targetApp);
    }

    /// <summary>拖拽松手：用真实卡替换占位卡并提交顺序；未移动则恢复原位。置 _justDragged 防止误启动。</summary>
    private void OnFavoritePressEnd(object sender, MouseButtonEventArgs e)
    {
        if (_justDragged)
        {
            _justDragged = false;
            _favoriteDragInProgress = false;
            return;
        }
        if (_favoriteDragInProgress)
        {
            _favoriteDragInProgress = false;
            if (_vm is not null)
                _vm.CommitFavoriteDrag();
            DestroyGhost();
            if (Shell.IsMouseCaptured) Shell.ReleaseMouseCapture();
            _dragApp = null;
            _dragOriginalIndex = -1;
            _justDragged = false;   // 关键：吞掉本次 MouseLeftButtonUp，避免冒泡到卡片触发 OnAppLaunch 误启动
            e.Handled = true;
            return;
        }
        CancelPress();
    }

    // ---- 拖拽视觉辅助 ----

    /// <summary>进入拖拽态：从收藏区移除被拖动卡（原位置消失），在原位插入占位卡；生成选中态残影并捕获鼠标。</summary>
    private void BeginDrag()
    {
        if (_dragApp is null || _vm is null) return;
        _dragOriginalIndex = _vm.Favorites.IndexOf(_dragApp);
        if (_dragOriginalIndex < 0) return;

        // 抓取偏移（光标在被拖动卡内的相对位置），用 DragCanvas 本地坐标，避免 TransformToVisual(null)
        var container = FavoritesGrid.ItemContainerGenerator.ContainerFromItem(_dragApp) as Visual;
        Point cardTl = new Point(0, 0);
        if (container is not null && DragCanvas is not null)
        {
            try { cardTl = container.TransformToVisual(DragCanvas).Transform(new Point(0, 0)); }
            catch { cardTl = new Point(0, 0); }
        }
        if (DragCanvas is not null)
        {
            var startLocal = DragCanvas.PointFromScreen(_favoriteDragStartPoint);
            _grabOffset = new Point(startLocal.X - cardTl.X, startLocal.Y - cardTl.Y);
        }

        // 进入两两交换拖拽：从收藏区移除被拖动卡，原位留下空卡占位（其余卡不动）
        _vm.BeginFavoriteDrag(_dragApp);
        _favoriteDragInProgress = true;
        _pressTimer.Stop();
        CreateGhost(_dragApp);
        if (!Shell.IsMouseCaptured) Shell.CaptureMouse();
        MoveGhostTo(_favoriteDragStartPoint);
    }

    /// <summary>生成跟随光标的残影：与卡片选中态一致（浅底 + 半透明黑叠加），缩小 40%。</summary>
    private void CreateGhost(AppEntry app)
    {
        DestroyGhost();
        var ghost = new Border
        {
            Width = 128,
            Height = 128,
            Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)), // 卡片底色
            IsHitTestVisible = false,
            Opacity = 0.95,
            LayoutTransform = new ScaleTransform(0.6, 0.6), // 拖动时缩小 40%
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black },
        };

        var inner = new Grid();
        var img = new System.Windows.Controls.Image
        {
            Width = 48, Height = 48, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Source = GetCardIcon(app),
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var name = new TextBlock
        {
            Text = app.Name,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120, ToolTip = app.Name,
        };
        // 选中态叠加（半透明黑），与卡片 hover 一致
        var overlay = new Border { Background = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)) };
        inner.Children.Add(img);
        inner.Children.Add(name);
        inner.Children.Add(overlay);
        ghost.Child = inner;

        DragCanvas.Children.Add(ghost);
        _dragGhost = ghost;
    }

    private void DestroyGhost()
    {
        if (_dragGhost is not null)
        {
            DragCanvas.Children.Remove(_dragGhost);
            _dragGhost = null;
        }
    }

    /// <summary>把残影左上角定位到（光标 - 抓取偏移）处，使抓取点固定在卡片内的原位。</summary>
    private void MoveGhostTo(Point screen)
    {
        if (_dragGhost is null) return;
        var local = DragCanvas.PointFromScreen(screen);
        var tl = new Point(local.X - _grabOffset.X, local.Y - _grabOffset.Y);
        Canvas.SetLeft(_dragGhost, tl.X);
        Canvas.SetTop(_dragGhost, tl.Y);
    }

    /// <summary>取卡片中已加载的真图标，用于残影显示（取不到则留空）。</summary>
    private ImageSource? GetCardIcon(AppEntry app)
    {
        var g = FindCardGrid(app);
        if (g is null) return null;
        var img = FindImage(g);
        return img?.Source;
    }

    private static System.Windows.Controls.Image? FindImage(DependencyObject o)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            if (c is System.Windows.Controls.Image im) return im;
            var nested = FindImage(c);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void ApplyShrink(AppEntry app, bool on)
    {
        var g = FindCardGrid(app);
        if (g is null) return;
        g.RenderTransformOrigin = new Point(0.5, 0.5);
        g.RenderTransform = on ? new ScaleTransform(0.85, 0.85) : null;
    }

    private Grid? FindCardGrid(AppEntry app)
    {
        var container = FavoritesGrid.ItemContainerGenerator.ContainerFromItem(app) as DependencyObject;
        return FindGridWithContext(container, app);
    }

    private static Grid? FindGridWithContext(DependencyObject? o, AppEntry app)
    {
        if (o is null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            if (c is Grid g && g.DataContext == app) return g;
            var nested = FindGridWithContext(c, app);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>清除所有收藏卡片可能残留的缩小变换（重排后容器会被回收复用）。</summary>
    private void ResetAllShrink()
    {
        foreach (var item in FavoritesGrid.Items)
        {
            var container = FavoritesGrid.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
            ClearCardScale(container);
        }
    }

    private static void ClearCardScale(DependencyObject? o)
    {
        if (o is null) return;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            if (c is Grid g) g.RenderTransform = null;
            ClearCardScale(c);
        }
    }

    private void CancelPress()
    {
        _pressTimer.Stop();
        _longPressArmed = false;
        _justDragged = false;
        // 若已进入拖拽态（占位卡已入列），恢复拖拽开始前的原始顺序（不落盘）
        if (_vm is not null)
            _vm.CancelFavoriteDrag();
        _dragApp = null;
        _dragOriginalIndex = -1;
        DestroyGhost();
        if (Shell.IsMouseCaptured) Shell.ReleaseMouseCapture();
    }

    /// <summary>
    /// 根据任务栏信息贴边放置面板。每次显示前都应调用，因为任务栏位置可能改变。
    /// </summary>
    public void PlaceAt(TaskbarInfo info)
    {
        _anchorMode = AnchorMode.Taskbar;
        _taskbarInfo = info;
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
        double h = GetContentHeight();
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
        Show();
        UpdateLayout();   // 强制布局：SizeToContent 下 ActualHeight 此时才反映当前内容高度
        PlaceForAnchor(info);   // 按设置（左下角 / 居中）定位，内部记录锚定方式
        _shownAt = DateTime.Now;
        Activate();
        SearchBox.Focus();
    }

    public void HidePanel()
    {
        _vm?.RebuildDock();   // M7-D：收起面板时兜底刷新 Dock（拖拽重排/取消后确保最新）
        Hide();
    }

    /// <summary>
    /// 取面板真实内容高度，用于 Dock / 任务栏锚定。
    /// SizeToContent=Height 下：窗体处于隐藏态时 ActualHeight 仍是上一次显示时的旧值
    /// （WPF 对 Visibility=Hidden 不刷新布局），若面板内容变高会偏小，导致定位偏低盖住 Dock。
    /// 故：已可见时直接读 ActualHeight；隐藏时强制对内容重新 Measure 取当前高度，并受窗口 MaxHeight 限制。
    /// </summary>
    private double GetContentHeight()
    {
        // 窗口高度已固定（见 XAML Height="640"），直接返回窗口高度：
        // ① 避免隐藏态按内容期望高度（收藏少时偏小）算锚点，导致面板实际更高而盖住 Dock；
        // ② 搜索/筛选时窗口高度不变，输入框与面板位置稳定，不再跳动。
        return Height;
    }

    /// <summary>
    /// 在指定屏幕点（WPF 逻辑坐标）正上方弹出面板，水平居中于该点。
    /// 由 Dock 中心按钮调用，实现“面板从 Dock 上方弹出”。
    /// </summary>
    public void PositionAbove(double anchorCenterLogicalX, double anchorCenterLogicalY)
    {
        _anchorMode = AnchorMode.Dock;
        _dockAnchorX = anchorCenterLogicalX;
        _dockAnchorY = anchorCenterLogicalY;
        double w = Width, h = GetContentHeight();
        double left = anchorCenterLogicalX - w / 2;
        double top = anchorCenterLogicalY - h - 8;

        double wl = SystemParameters.WorkArea.Left;
        double wt = SystemParameters.WorkArea.Top;
        double wr = SystemParameters.WorkArea.Right;
        double wb = SystemParameters.WorkArea.Bottom;
        if (left + w > wr) left = wr - w;
        if (left < wl) left = wl;
        if (top + h > wb) top = wb - h;
        if (top < wt) top = wt;

        Left = left;
        Top = top;
    }

    /// <summary>按设置（左下角贴任务栏 / 屏幕底部居中）定位由任务栏或热键唤起的面板。</summary>
    private void PlaceForAnchor(TaskbarInfo info)
    {
        if (PanelAnchor == PanelPosition.Center)
        {
            PositionCenter();
        }
        else
        {
            _anchorMode = AnchorMode.Taskbar;
            _taskbarInfo = info;
            PlaceAt(info);
        }
    }

    /// <summary>屏幕底部水平居中弹出（忽略任务栏位置）。</summary>
    private void PositionCenter()
    {
        _anchorMode = AnchorMode.Center;
        _taskbarInfo = null;
        double w = Width, h = GetContentHeight();
        double wl = SystemParameters.WorkArea.Left;
        double wr = SystemParameters.WorkArea.Right;
        double wb = SystemParameters.WorkArea.Bottom;
        double margin = 8;
        double left = (wl + wr) / 2 - w / 2;
        double top  = wb - h - margin;
        if (left < wl) left = wl;
        if (left + w > wr) left = wr - w;
        Left = left;
        Top = top;
    }

    /// <summary>
    /// 内容高度变化（如搜索筛选使结果变少/清空搜索使结果变多）时，面板 SizeToContent 改了高度，
    /// 但 Top 未变会导致底边下移盖住 Dock。此处按记录的锚定方式重新定位，使底边保持贴住
    /// Dock 顶部 / 任务栏上沿（高度变大→向上长，高度变小→整体略微下移但底边不动）。
    /// </summary>
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || _anchorMode == AnchorMode.None || _reanchoring) return;
        _reanchoring = true;
        try
        {
            if (_anchorMode == AnchorMode.Dock)
                PositionAbove(_dockAnchorX, _dockAnchorY);
            else if (_anchorMode == AnchorMode.Center)
                PositionCenter();
            else if (_anchorMode == AnchorMode.Taskbar && _taskbarInfo is not null)
                PlaceAt(_taskbarInfo);
        }
        finally
        {
            _reanchoring = false;
        }
    }

    /// <summary>
    /// 面板失焦即收起（拖拽收藏中除外）。不依赖“按下标记”时序：改用前台窗口判定——
    /// 若失焦后前台仍是本进程（即点到了 Dock 的中心按钮 / 收藏图标 / 「+」），不收起，
    /// 交给中心按钮抬起的 OnCenterButtonUp 决定开合，避免“点按钮失焦先收起、抬起又重开”的竞态；
    /// 若失焦到别的进程（其它应用 / 桌面 / 任务栏），正常收起，也直接解决
    /// “面板开着、Dock 被应用盖住时点不到中心按钮关闭”的卡死（从别处开应用即失焦收起）。
    /// </summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_favoriteDragInProgress) { CancelPress(); return; }
        if (IsForegroundOwnProcess()) return;   // 失焦到本进程 Dock：交给中心按钮抬起逻辑，不抢收
        HidePanel();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>当前前台窗口是否属于本进程（即 Dock 窗体）。用于区分“点中心按钮”与“点别的程序”。</summary>
    private static bool IsForegroundOwnProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out uint pid);
        return pid == Environment.ProcessId;
    }
}

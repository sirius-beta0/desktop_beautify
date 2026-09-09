using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Favorites;
using Launcher.Core.Indexing;

namespace Launcher.UI;

/// <summary>
/// 面板的数据上下文。M2 承载扫描得到的应用列表与搜索过滤；M5 加入收藏区（置顶、可拖拽排序）
/// 与搜索态（输入时收藏区隐藏、结果含被收藏项）。
/// 为避免依赖 WPF 的 ICollectionView（MarkupCompile 临时工程引用集不全），这里直接用
/// ObservableCollection 并在代码内做过滤，WPF 仍可正常绑定。
/// </summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private readonly ObservableCollection<AppEntry> _allApps = new();
    private readonly ObservableCollection<AppEntry> _favorites = new();
    private readonly ObservableCollection<AppEntry> _main = new();
    private readonly Dictionary<string, AppEntry> _byId = new();

    private FavoriteStore? _store;
    private bool _searchActive;

    /// <summary>收藏区：按收藏顺序（用户拖拽结果）排列，仅含能解析到的应用。</summary>
    public ObservableCollection<AppEntry> Favorites => _favorites;

    /// <summary>普通区：无搜索时显示未收藏项；有搜索时显示全部匹配项（含被收藏项）。</summary>
    public ObservableCollection<AppEntry> Main => _main;

    /// <summary>Dock 栏中心按钮左侧的收藏（奇数索引 fav[1],fav[3]…，从中心向外）。</summary>
    public ObservableCollection<AppEntry> DockLeft { get; } = new();

    /// <summary>Dock 栏中心按钮右侧的收藏（偶数索引 fav[0],fav[2]…，从中心向外；越靠前越靠近中心）。</summary>
    public ObservableCollection<AppEntry> DockRight { get; } = new();

    /// <summary>Dock 栏最多展示的收藏数量。</summary>
    private const int DockMaxCount = 16;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isIndexing = true;

    /// <summary>收藏区 + 分隔是否可见：仅在无搜索且有收藏时为 true。</summary>
    [ObservableProperty]
    private bool _showFavorites;

    public PanelViewModel(FavoriteStore? store = null)
    {
        _store = store;
        Rebuild();
    }

    /// <summary>用扫描结果替换全部应用。</summary>
    public void SetApps(IEnumerable<AppEntry> apps)
    {
        _allApps.Clear();
        _byId.Clear();
        foreach (var a in apps)
        {
            _allApps.Add(a);
            _byId[a.Id] = a;
        }
        IsIndexing = false;
        Rebuild();
    }

    /// <summary>依据收藏存储与搜索态重建收藏区与普通区。</summary>
    private void Rebuild()
    {
        // 收藏区：按存储顺序
        _favorites.Clear();
        if (_store is not null)
            foreach (var id in _store.Ids)
                if (_byId.TryGetValue(id, out var app))
                    _favorites.Add(app);

        // 普通区：搜索态=全部匹配；非搜索态=未收藏项（收藏项已在上方分区）
        _main.Clear();
        foreach (var a in _allApps)
        {
            if (_searchActive)
            {
                if (Matches(a, SearchText)) _main.Add(a);
            }
            else if (!IsFavorite(a))
            {
                _main.Add(a);
            }
        }
        UpdateShowFavorites();
        RebuildDock();
    }

    private static bool Matches(AppEntry e, string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return true;
        return e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    public bool IsFavorite(AppEntry e) => _store is not null && _store.Contains(e.Id);

    /// <summary>右键菜单的收藏 / 取消收藏切换。</summary>
    public void ToggleFavorite(AppEntry app)
    {
        if (_store is null) return;
        if (_store.Contains(app.Id)) _store.Remove(app.Id);
        else _store.Add(app.Id);
        Rebuild();
    }

    /// <summary>
    /// 把已收藏项移到收藏列表最前（右键菜单「移到前面」）。
    /// 未收藏或已是首位的项不做处理；Move 内部会落盘，Rebuild 会同步刷新 Dock 顺序。
    /// </summary>
    public void MoveFavoriteToFront(AppEntry app)
    {
        if (_store is null) return;
        var idx = _store.IndexOf(app.Id);
        if (idx <= 0) return;      // 未收藏(-1) 或 已是首位(0)
        _store.Move(idx, 0);
        Rebuild();
    }

    // ---- 收藏区两两交换拖拽（M5 优化：占位卡始终跟随光标所在槽位）----
    private List<string>? _dragOriginalOrder;   // 拖拽开始时的收藏顺序快照（原始下标基准）
    private string? _dragSourceId;
    private int _dragHomeIndex = -1;            // 源卡原位（原始下标）
    private int _dragLandingIndex = -1;         // 源卡当前落点 = 占位卡所在槽位（初始为 home）
    private AppEntry? _dragPlaceholder;
    private const string DragPlaceholderId = "__drag_placeholder__";

    /// <summary>开始交换拖拽：记录起始顺序，被拖动卡从收藏区移除（变为抬起态），原位留下空卡占位。</summary>
    public void BeginFavoriteDrag(AppEntry source)
    {
        if (_store is null) return;
        _dragOriginalOrder = _store.Ids.ToList();
        _dragSourceId = source.Id;
        _dragHomeIndex = _dragOriginalOrder.IndexOf(source.Id);
        if (_dragHomeIndex < 0) return;
        _dragPlaceholder = new AppEntry { Id = DragPlaceholderId, Name = "", IsPlaceholder = true };
        _dragLandingIndex = _dragHomeIndex;
        ApplyDragLayout(_dragLandingIndex);
    }

    /// <summary>
    /// 光标进入另一张真实卡：以该卡「当前所在槽位」作为源卡落点——占位卡移到该槽，
    /// 该槽原位的卡（按原始顺序即 original[落点]）被换到源卡原位 home。
    /// 用「当前槽位」而非「卡的原始下标」做落点，这样来回拖动时占位卡始终跟随光标所在槽，
    /// 而非粘在卡片身份上（修复：拖到卡片4后再移回，原卡片3槽位不变占位的 bug）。
    /// 同一槽位再次进入不重建；离开卡片（target=null）保持上一状态，实现“循环直到松手”。
    /// </summary>
    public void SetFavoriteDragTarget(AppEntry? target)
    {
        if (_dragOriginalOrder is null || _dragSourceId is null || _dragPlaceholder is null) return;
        if (target is null || target.IsPlaceholder || target.Id == _dragSourceId) return;
        int slot = _favorites.IndexOf(target);          // 该卡在当前显示中的槽位
        if (slot < 0 || slot == _dragLandingIndex) return;
        _dragLandingIndex = slot;
        ApplyDragLayout(slot);
    }

    /// <summary>松手落定：用真实卡替换空卡，把当前（仅含真实项）顺序写回存储并持久化。</summary>
    public void CommitFavoriteDrag()
    {
        if (_dragOriginalOrder is null || _dragSourceId is null || _dragPlaceholder is null || _store is null) return;
        int phIdx = _favorites.IndexOf(_dragPlaceholder);
        if (phIdx >= 0)
        {
            _favorites.Remove(_dragPlaceholder);
            if (_byId.TryGetValue(_dragSourceId, out var src)) _favorites.Insert(phIdx, src);
        }
        _store.Replace(_favorites.Select(a => a.Id).ToList());
        ClearDragState();
        UpdateShowFavorites();
        RebuildDock();   // M7-D：拖拽重排落定后实时刷新 Dock（收藏顺序变了）
    }

    /// <summary>取消拖拽：恢复拖拽开始前的原始顺序（不落盘）。</summary>
    public void CancelFavoriteDrag()
    {
        if (_dragOriginalOrder is null) return;
        _favorites.Clear();
        foreach (var id in _dragOriginalOrder)
            if (_byId.TryGetValue(id, out var app)) _favorites.Add(app);
        ClearDragState();
    }

    private void ClearDragState()
    {
        _dragOriginalOrder = null;
        _dragSourceId = null;
        _dragHomeIndex = -1;
        _dragLandingIndex = -1;
        _dragPlaceholder = null;
    }

    /// <summary>
    /// 按「占位卡落在 landingIndex 槽、该槽原位的卡换到源卡原位 home」重建显示。
    /// 始终以原始顺序为基准重建：除 home 与 landingIndex 两个槽外，其余槽位保持原始卡，
    /// 因此来回拖动能正确回退/前进，占位卡始终在光标所在槽（满足“鼠标移到哪哪就占位”）。
    /// </summary>
    private void ApplyDragLayout(int landingIndex)
    {
        if (_dragOriginalOrder is null || _dragSourceId is null || _dragPlaceholder is null) return;
        int n = _dragOriginalOrder.Count;
        if (landingIndex < 0 || landingIndex >= n) return;

        var result = new AppEntry[n];
        // 1) 原始顺序填满；源卡被抬起的位置先放占位
        for (int i = 0; i < n; i++)
        {
            var id = _dragOriginalOrder[i];
            result[i] = id == _dragSourceId ? _dragPlaceholder
                       : _byId.TryGetValue(id, out var app) ? app : _dragPlaceholder;
        }

        // 2) 光标所在槽 = 占位卡（源将落于此）
        result[landingIndex] = _dragPlaceholder;

        // 3) 该槽原位的卡换到源卡原位（落点==原位时无需位移）
        if (landingIndex != _dragHomeIndex)
        {
            var landedId = _dragOriginalOrder[landingIndex];
            result[_dragHomeIndex] = landedId == _dragSourceId
                ? _dragPlaceholder
                : (_byId.TryGetValue(landedId, out var app) ? app : _dragPlaceholder);
        }

        _favorites.Clear();
        foreach (var item in result)
            _favorites.Add(item ?? _dragPlaceholder);
    }

    private void UpdateShowFavorites() => ShowFavorites = !_searchActive && _favorites.Count > 0;

    /// <summary>
    /// 重建 Dock 栏两侧的收藏集合：取前 16 个，偶数索引入右侧、奇数索引入左侧，
    /// 使“越靠前的收藏越靠近中心按钮”（D3 居中对称布局）。拖拽占位卡不进入 Dock。
    /// M7-C：图标个数为奇数时，短侧补一个透明「+」占位（点击打开搜索面板），
    /// 使 Dock 与面板中心对齐。占位不写入存储、仅存在于 Dock 集合，下次重建即重算。
    /// </summary>
    internal void RebuildDock()
    {
        DockLeft.Clear();
        DockRight.Clear();
        int n = Math.Min(DockMaxCount, _favorites.Count);
        for (int i = 0; i < n; i++)
        {
            var app = _favorites[i];
            if (app.IsPlaceholder) continue;
            if (i % 2 == 0) DockRight.Add(app);
            else DockLeft.Add(app);
        }

        // M7-C：奇数个时两侧数量不等，给少的一侧补「+」占位，重新居中
        if (DockLeft.Count < DockRight.Count) DockLeft.Add(MakeDockAdd());
        else if (DockRight.Count < DockLeft.Count) DockRight.Add(MakeDockAdd());
    }

    /// <summary>Dock 栏「添加收藏」占位项（透明 + 号，点击打开搜索面板）。</summary>
    private static AppEntry MakeDockAdd() => new() { Id = "__dock_add__", Name = "+", IsDockAdd = true };

    partial void OnSearchTextChanged(string value)
    {
        _searchActive = !string.IsNullOrWhiteSpace(value);
        Rebuild();
    }
}

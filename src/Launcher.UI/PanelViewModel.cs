using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Indexing;

namespace Launcher.UI;

/// <summary>
/// 面板的数据上下文。M2 承载扫描得到的应用列表与搜索过滤；
/// 收藏与排序逻辑在 M5 补全。
/// </summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private readonly ObservableCollection<AppEntry> _allApps = new();
    private readonly ICollectionView _appsView;

    public PanelViewModel()
    {
        _appsView = CollectionViewSource.GetDefaultView(_allApps);
        _appsView.Filter = OnFilter;
    }

    /// <summary>经过搜索过滤后供网格绑定的视图。</summary>
    public ICollectionView AppsView => _appsView;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isIndexing = true;

    /// <summary>用扫描结果替换全部应用。</summary>
    public void SetApps(IEnumerable<AppEntry> apps)
    {
        _allApps.Clear();
        foreach (var a in apps)
        {
            _allApps.Add(a);
        }
        IsIndexing = false;
        _appsView.Refresh();
    }

    private bool OnFilter(object obj)
    {
        if (obj is not AppEntry e) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return e.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    partial void OnSearchTextChanged(string value) => _appsView.Refresh();
}

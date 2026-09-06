using System.Text.Json;

namespace Launcher.Core.Indexing;

/// <summary>
/// M4 应用索引器：并行扫描开始菜单与 AppsFolder，合并去重，并落地 JSON 缓存
/// 以实现冷启动秒开（先渲染缓存，后台全量扫描后增量刷新）。
///
/// 去重规则（与需求文档 Q1=B 一致）：
/// - 冲突时**保留 StartMenu 来源**（显示名/启动参数/工作目录更友好），AppsFolder 作补充。
/// - 按 Id 去重（UWP 的 AUMID 与 Win32 的 TargetPath 各成体系）。
/// - 按显示名兜底去重：Desktop Bridge 应用（既是 Win32 又在 AppsFolder）在两处的
///   显示名相同，避免重复项。
/// </summary>
public sealed class AppIndexer
{
    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopBeautify");

    private static readonly string CachePath = Path.Combine(CacheDir, "appindex.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>后台全量扫描两个数据源并合并去重，结果写回缓存后返回。</summary>
    public async Task<IReadOnlyList<AppEntry>> BuildAsync(CancellationToken ct = default)
    {
        var startMenuTask = new StartMenuScanner().ScanAsync(ct);
        var appsFolderTask = new AppsFolderScanner().ScanAsync(ct);
        await Task.WhenAll(startMenuTask, appsFolderTask).ConfigureAwait(false);

        var merged = Merge(startMenuTask.Result, appsFolderTask.Result);
        SaveCache(merged);
        return merged;
    }

    /// <summary>尝试读取上次扫描的缓存用于冷启动秒开；文件缺失或损坏返回 null。</summary>
    public IReadOnlyList<AppEntry>? LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var list = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(CachePath));
                if (list is { Count: > 0 }) return list;
            }
        }
        catch
        {
            // 缓存损坏则忽略，交给全量扫描
        }

        return null;
    }

    private static IReadOnlyList<AppEntry> Merge(
        IReadOnlyList<AppEntry> startMenu, IReadOnlyList<AppEntry> appsFolder)
    {
        var byId = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        var startNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in startMenu)
        {
            byId[e.Id] = e;        // 开始菜单先入，作为冲突时的优先来源
            startNames.Add(e.Name);
        }

        foreach (var e in appsFolder)
        {
            if (byId.ContainsKey(e.Id)) continue;          // Id 已存在（开始菜单优先）
            if (startNames.Contains(e.Name)) continue;      // 同名兜底（Desktop Bridge 去重）
            byId[e.Id] = e;
        }

        return byId.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void SaveCache(IReadOnlyList<AppEntry> apps)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(apps, JsonOptions));
        }
        catch
        {
            // 缓存写入失败不影响主流程
        }
    }
}

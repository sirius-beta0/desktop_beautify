using Launcher.Core.Platform;

namespace Launcher.Core.Indexing;

/// <summary>
/// 递归扫描开始菜单的 .lnk 快捷方式。覆盖 Win32 应用；UWP 由 M4 的 AppsFolder 补全。
/// </summary>
public sealed class StartMenuScanner : IAppScanner
{
    public Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default)
        => Task.Run(() => Scan(ct), ct);

    private static IReadOnlyList<AppEntry> Scan(CancellationToken ct)
    {
        var entries = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, source) in StartMenuPaths.Roots())
        {
            foreach (var linkPath in SafeEnumerateLinks(root))
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileNameWithoutExtension(linkPath);
                if (IsNoise(name)) continue;

                var info = LinkResolver.Resolve(linkPath);
                if (info?.TargetPath is null) continue;

                // 同一路径在两个目录都出现时保留先扫到的（系统级优先）
                if (!seen.Add(info.TargetPath)) continue;

                entries.Add(new AppEntry
                {
                    Id = AppEntryId.Compute(info.TargetPath),
                    Name = name,
                    TargetPath = info.TargetPath,
                    Arguments = info.Arguments,
                    WorkingDirectory = info.WorkingDirectory,
                    IconPath = info.IconPath,
                    IconIndex = info.IconIndex,
                    Source = source,
                    LinkPath = linkPath,
                    IsValid = File.Exists(info.TargetPath) || Directory.Exists(info.TargetPath),
                });
            }
        }

        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IEnumerable<string> SafeEnumerateLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.lnk");
            }
            catch
            {
                continue;   // 无权限目录，跳过
            }

            foreach (var f in files)
            {
                yield return f;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var d in subdirs)
            {
                pending.Push(d);
            }
        }
    }

    /// <summary>
    /// 卸载程序、帮助文档这类不是"应用"的快捷方式，直接过滤掉。
    /// 只过滤明显的噪声，避免误伤正常应用。
    /// </summary>
    private static bool IsNoise(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("uninstall")
            || n.Contains("卸载")
            || n.Contains("uninst");
    }
}
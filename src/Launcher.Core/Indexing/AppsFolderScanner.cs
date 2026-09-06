using Vanara.Windows.Shell;

namespace Launcher.Core.Indexing;

/// <summary>
/// 枚举 shell:AppsFolder（FOLDERID_AppsFolder），覆盖 Win32 + UWP + 商店应用。
/// 开始菜单的 .lnk 只覆盖传统 Win32 安装包，UWP / 商店应用只在这里出现。
/// 与 <see cref="StartMenuScanner"/> 的产出合并去重后（见 <see cref="AppIndexer"/>），
/// UWP 应用才会出现在启动器列表里。
/// </summary>
public sealed class AppsFolderScanner : IAppScanner
{
    public Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default)
        => Task.Run(() => Scan(ct), ct);

    private static IReadOnlyList<AppEntry> Scan(CancellationToken ct)
    {
        var entries = new List<AppEntry>();
        try
        {
            // AppsFolder 运行时即 ShellFolder 类型，转后可用其公共枚举器
            using var folder = (ShellFolder)ShellItem.Open("shell:appsFolder");
            foreach (var item in folder)
            {
                ct.ThrowIfCancellationRequested();

                // 解析名即 AUMID（形如 PackageFamilyName!AppId）；容器/子菜单项无 "!" 跳过。
                var aumid = item.ParsingName;
                if (string.IsNullOrWhiteSpace(aumid) || !aumid.Contains('!')) continue;

                var name = item.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                entries.Add(new AppEntry
                {
                    Id = AppEntryId.Compute(aumid),
                    Name = name,
                    AppUserModelId = aumid,
                    IconPath = "shell:appsFolder\\" + aumid,   // IconExtractor 据此以 shell: 命名空间提取 UWP 图标
                    Source = AppSource.AppsFolder,
                    IsValid = true,
                });
            }
        }
        catch
        {
            // AppsFolder 理论上 Win10/11 必有；极端不可用时回退为空列表，由开始菜单兜底
        }

        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

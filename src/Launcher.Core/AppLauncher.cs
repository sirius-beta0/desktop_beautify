using System.Diagnostics;
using System.IO;
using Launcher.Core.Indexing;

namespace Launcher.Core;

/// <summary>
/// 根据 AppEntry 启动应用。Win32 走 Process.Start；UWP 走 explorer shell:appsFolder。
/// </summary>
public static class AppLauncher
{
    public static bool Launch(AppEntry entry)
    {
        try
        {
            if (!string.IsNullOrEmpty(entry.AppUserModelId))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:appsFolder\\{entry.AppUserModelId}")
                {
                    UseShellExecute = true,
                });
                return true;
            }

            if (!string.IsNullOrEmpty(entry.TargetPath))
            {
                var psi = new ProcessStartInfo(entry.TargetPath)
                {
                    UseShellExecute = true,
                    Arguments = entry.Arguments ?? "",
                    WorkingDirectory = !string.IsNullOrWhiteSpace(entry.WorkingDirectory)
                        ? entry.WorkingDirectory
                        : Path.GetDirectoryName(entry.TargetPath) ?? "",
                };
                Process.Start(psi);
                return true;
            }
        }
        catch
        {
            // 启动失败（文件被删、权限不足等）由调用方通过 IsValid 标记处理
            return false;
        }

        return false;
    }

    /// <summary>「打开文件位置」可定位到的路径：优先真实目标 exe，其次快捷方式本体；都不可用则返回 null。</summary>
    public static string? ResolveLocationPath(AppEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.TargetPath) && File.Exists(entry.TargetPath))
            return entry.TargetPath;
        if (!string.IsNullOrEmpty(entry.LinkPath) && File.Exists(entry.LinkPath))
            return entry.LinkPath;
        return null;
    }

    /// <summary>
    /// 在资源管理器中定位并选中该应用的文件（右键菜单「打开文件位置」）。
    /// UWP 等无实体文件的条目无法定位，返回 false。
    /// </summary>
    public static bool OpenFileLocation(AppEntry entry)
    {
        var path = ResolveLocationPath(entry);
        if (path is null) return false;
        try
        {
            // explorer /select, 后面紧跟路径；路径含空格时本写法仍可靠（整串作为一个参数）
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>可否以管理员身份运行：需要真实 exe。UWP 应用不允许提权，故为 false。</summary>
    public static bool CanRunAsAdmin(AppEntry entry) => !string.IsNullOrEmpty(entry.TargetPath);

    /// <summary>
    /// 以管理员身份启动（触发 UAC 提权）。仅 Win32 可行 —— UWP 没有可提权的 exe。
    /// 用户在 UAC 弹窗点「否」会抛异常，属正常取消，按失败返回。
    /// </summary>
    public static bool LaunchAsAdmin(AppEntry entry)
    {
        if (!CanRunAsAdmin(entry)) return false;
        try
        {
            var psi = new ProcessStartInfo(entry.TargetPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = entry.Arguments ?? "",
                WorkingDirectory = !string.IsNullOrWhiteSpace(entry.WorkingDirectory)
                    ? entry.WorkingDirectory
                    : Path.GetDirectoryName(entry.TargetPath!) ?? "",
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

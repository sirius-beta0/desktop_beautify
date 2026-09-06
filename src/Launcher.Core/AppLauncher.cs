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
}

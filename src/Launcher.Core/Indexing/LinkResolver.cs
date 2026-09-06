using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Launcher.Core.Indexing;

/// <summary>
/// .lnk 解析结果。字段都可能为空 —— 快捷方式可能指向已卸载的程序。
/// </summary>
public sealed record LinkInfo(
    string? TargetPath,
    string? Arguments,
    string? WorkingDirectory,
    string? Description,
    string? IconPath,
    int IconIndex);

/// <summary>
/// 用 IShellLink COM 接口解析 .lnk。
/// 不要手工解析 lnk 二进制格式 —— 那是有版本差异的内部结构。
/// </summary>
public static class LinkResolver
{
    public static LinkInfo? Resolve(string linkPath)
    {
        try
        {
            // NoUI：解析失败时不要弹任何系统对话框
            using var link = new ShellLink(linkPath, LinkResolution.NoUI, default, TimeSpan.FromSeconds(1));

            var icon = link.IconLocation;
            string? iconPath = null;
            int iconIndex = 0;
            if (icon is { IsValid: true })
            {
                iconPath = NullIfEmpty(icon.ModuleFileName);
                iconIndex = icon.ResourceId;
            }

            return new LinkInfo(
                TargetPath: Expand(NullIfEmpty(link.TargetPath)),
                Arguments: NullIfEmpty(link.Arguments),
                WorkingDirectory: Expand(NullIfEmpty(link.WorkingDirectory)),
                Description: NullIfEmpty(link.Description),
                IconPath: iconPath,
                IconIndex: iconIndex);
        }
        catch
        {
            // 损坏的快捷方式、权限问题等，跳过即可
            return null;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// 展开 %WINDIR% / %ProgramFiles% 等环境变量。
    /// 系统工具（字符映射表、组件服务等）的 lnk 目标常带 %windir%，
    /// 不展开会导致 File.Exists 误判失效、Process.Start 启动失败。
    /// </summary>
    private static string? Expand(string? s) => s is null ? null : Environment.ExpandEnvironmentVariables(s);
}
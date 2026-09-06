namespace Launcher.Core.Indexing;

/// <summary>
/// 应用条目的来源。M4 合并 AppsFolder 后用于去重优先级判断。
/// </summary>
public enum AppSource
{
    /// <summary>系统级开始菜单（%ProgramData%），多数传统安装包落在这里。</summary>
    CommonStartMenu,

    /// <summary>用户级开始菜单（%AppData%）。</summary>
    UserStartMenu,

    /// <summary>shell:AppsFolder 虚拟目录，覆盖 Win32 + UWP + 商店应用。</summary>
    AppsFolder,
}

/// <summary>
/// 单个应用条目。Id 为稳定标识，用于收藏引用与去重。
/// </summary>
public sealed class AppEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Win32 目标可执行文件路径。</summary>
    public string? TargetPath { get; init; }

    public string? Arguments { get; init; }

    /// <summary>工作目录。相当多程序依赖它才能正常启动，不能省略。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>UWP 应用的 AppUserModelID。来自 AppsFolder 时才有。</summary>
    public string? AppUserModelId { get; init; }

    /// <summary>图标所在文件（M3 用于提取）。</summary>
    public string? IconPath { get; init; }

    /// <summary>图标在文件中的资源索引。</summary>
    public int IconIndex { get; init; }

    public AppSource Source { get; init; }

    /// <summary>快捷方式自身路径，便于后续重新解析或定位。</summary>
    public string? LinkPath { get; init; }

    public int LaunchCount { get; set; }

    public DateTime? LastLaunch { get; set; }

    /// <summary>目标是否仍然存在（程序被卸载后为 false）。</summary>
    public bool IsValid { get; set; } = true;
}
namespace Launcher.Core.Platform;

/// <summary>
/// 开始菜单快捷方式目录。
/// 只扫用户级会漏掉大批软件 —— 多数传统安装包落在系统级目录。
/// </summary>
public static class StartMenuPaths
{
    /// <summary>%AppData%\Microsoft\Windows\Start Menu\Programs</summary>
    public static string? UserPrograms =>
        Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    /// <summary>%ProgramData%\Microsoft\Windows\Start Menu\Programs</summary>
    public static string? CommonPrograms =>
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

    public static IEnumerable<(string Path, Indexing.AppSource Source)> Roots()
    {
        var common = CommonPrograms;
        if (!string.IsNullOrEmpty(common) && Directory.Exists(common))
            yield return (common, Indexing.AppSource.CommonStartMenu);

        var user = UserPrograms;
        if (!string.IsNullOrEmpty(user) && Directory.Exists(user))
            yield return (user, Indexing.AppSource.UserStartMenu);
    }
}
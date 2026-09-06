namespace Launcher.Core;

/// <summary>
/// 应用级静态信息（关于页 / 设置页共用，避免 UI 反向引用 Launcher.App 造成循环依赖）。
/// </summary>
public static class AppInfo
{
    /// <summary>当前版本（与 Launcher.App.csproj 的 &lt;Version&gt; 保持一致）。</summary>
    public const string Version = "0.1.0";

    /// <summary>作者。</summary>
    public const string Author = "sirius-beta0";

    /// <summary>开源许可证。</summary>
    public const string License = "Apache-2.0";

    /// <summary>GitHub 仓库地址。</summary>
    public const string GitHubUrl = "https://github.com/sirius-beta0/desktop_beautify";

    /// <summary>许可证全称（关于页展示）。</summary>
    public const string LicenseFullName = "Apache License 2.0";
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using Launcher.Core;

namespace Launcher.UI;

public partial class AboutWindow : Window
{
    /// <summary>
    /// 在关于页展示的第三方开源组件（与仓库根 THIRD-PARTY-NOTICES.md 保持一致）。
    /// 版本号取自 dotnet list package --include-transitive。
    /// </summary>
    public static IReadOnlyList<OssDependency> ThirdParty { get; } = new List<OssDependency>
    {
        new("Vanara.Windows.Shell", "5.0.7", "MIT", "© 2017–2026 David Hall"),
        new("CommunityToolkit.Mvvm", "8.4.2", "MIT", "© .NET Foundation and Contributors"),
        new("Vanara.*（PInvoke / Windows 系列，传递依赖）", "5.0.7", "MIT", "© 2017–2026 David Hall"),
        new("Microsoft System.*（传递依赖）", "10.0.2", "MIT", "© Microsoft Corporation"),
        new(".NET 8 运行时（自包含发布一并打包）", "8.0", "MIT", "© .NET Foundation and Contributors"),
    };

    public AboutWindow()
    {
        InitializeComponent();
        // NavigateUri 是 Uri 类型，x:Static 直接赋字符串会因类型转换失败抛 XamlParseException；
        // 改在代码后置用 new Uri(...) 赋值，配合 RequestNavigate 在浏览器打开。
        GitHubLink.NavigateUri = new Uri(AppInfo.GitHubUrl);
        // 优先指向随程序分发的声明文件；开发/未随附时退回仓库。
        var local = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        NoticesLink.NavigateUri = new Uri(File.Exists(local)
            ? local
            : AppInfo.GitHubUrl + "/blob/main/THIRD-PARTY-NOTICES.md");
    }

    private void OnGitHubNavigate(object sender, RequestNavigateEventArgs e) => OpenTarget(e.Uri);

    private void OnOpenNotices(object sender, RequestNavigateEventArgs e) => OpenTarget(e.Uri);

    private static void OpenTarget(Uri uri)
    {
        try
        {
            // 本地文件（file://）与 http(s) 均交给系统默认程序打开。
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 无默认程序等极端情况忽略
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

/// <summary>关于页「第三方开源组件」列表的一项。</summary>
public sealed record OssDependency(string Name, string Version, string License, string Copyright);

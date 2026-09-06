using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Launcher.Core;

namespace Launcher.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        // NavigateUri 是 Uri 类型，x:Static 直接赋字符串会因类型转换失败抛 XamlParseException；
        // 改在代码后置用 new Uri(...) 赋值，配合 RequestNavigate 在浏览器打开。
        GitHubLink.NavigateUri = new Uri(AppInfo.GitHubUrl);
    }

    private void OnGitHubNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 无默认浏览器等极端情况忽略
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

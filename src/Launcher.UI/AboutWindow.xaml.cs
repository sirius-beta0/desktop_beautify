using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Launcher.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
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

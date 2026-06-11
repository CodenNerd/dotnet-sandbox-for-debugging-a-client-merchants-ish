using System.Windows;

namespace LegacyWebView.BugRepro;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        BrowserEmulation.EnsureIe11Emulation();
        IeSecurityConfig.Apply();
        new MainWindow().Show();
    }
}

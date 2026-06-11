using System.IO;
using Microsoft.Win32;

namespace LegacyWebView.BugRepro;

internal static class BrowserEmulation
{
    private const int Ie11EdgeMode = 11001;

    public static void EnsureIe11Emulation()
    {
        var exeName = Path.GetFileName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(exeName))
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
        key?.SetValue(exeName, Ie11EdgeMode, RegistryValueKind.DWord);
    }
}

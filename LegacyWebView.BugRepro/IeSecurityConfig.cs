using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LegacyWebView.BugRepro;

internal static class IeSecurityConfig
{
    private const int FeatureLocalMachineLockdown = 0x15;
    private const int SetFeatureOnProcess = 0x2;

    public static void Apply()
    {
        DisableLocalMachineLockdown();
        TrustLocalhost();
        TrustLoopbackRange();
    }

    private static void DisableLocalMachineLockdown()
    {
        try
        {
            _ = CoInternetSetFeatureEnabled(
                FeatureLocalMachineLockdown,
                SetFeatureOnProcess,
                false);
        }
        catch
        {
        }
    }

    private static void TrustLocalhost()
    {
        using var localhost = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap\Domains\localhost");
        localhost?.SetValue("http", 2, RegistryValueKind.DWord);
    }

    private static void TrustLoopbackRange()
    {
        using var ranges = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap\Ranges\Range127");
        ranges?.SetValue(":Range", "127.0.0.1");
        ranges?.SetValue("http", 2, RegistryValueKind.DWord);
    }

    [DllImport("urlmon.dll", ExactSpelling = true)]
    private static extern int CoInternetSetFeatureEnabled(int featureEntry, int dwFlags, bool fEnable);
}

using System.Reflection;
using System.Windows.Controls;

namespace LegacyWebView.BugRepro;

internal static class WebBrowserHelper
{
    public static void SuppressScriptErrors(WebBrowser browser, bool suppress)
    {
        var field = typeof(WebBrowser).GetField(
            "_axIWebBrowser2",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(browser) is not object webBrowser)
        {
            return;
        }

        webBrowser.GetType().InvokeMember(
            "Silent",
            BindingFlags.SetProperty,
            null,
            webBrowser,
            [suppress]);
    }
}

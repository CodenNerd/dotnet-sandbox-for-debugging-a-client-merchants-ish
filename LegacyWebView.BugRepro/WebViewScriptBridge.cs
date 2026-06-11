using System.Runtime.InteropServices;

namespace LegacyWebView.BugRepro;

[ComVisible(true)]
public class WebViewScriptBridge
{
    private readonly MainWindow _window;

    public WebViewScriptBridge(MainWindow window)
    {
        _window = window;
    }

    public void WebViewPostMessage(string json) => _window.LogFromScript($"WebMessageReceived: {json}");

    public void LogConsole(string level, string message) => _window.LogFromScript($"Console[{level}] - {message}");
}

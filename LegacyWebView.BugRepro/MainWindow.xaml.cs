using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace LegacyWebView.BugRepro;

public partial class MainWindow : Window
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly WebViewScriptBridge _scriptBridge;

    private const string ConsoleHookScript = """
        (function () {
            if (window.__legacyConsoleHookInstalled) return;
            window.__legacyConsoleHookInstalled = true;
            ['log', 'warn', 'error', 'info'].forEach(function (level) {
                var original = console[level];
                console[level] = function () {
                    var message = Array.prototype.slice.call(arguments).join(' ');
                    try {
                        if (window.external && window.external.LogConsole) {
                            window.external.LogConsole(level, message);
                        }
                    } catch (e) { }
                    original.apply(console, arguments);
                };
            });
        })();
        """;

    public MainWindow()
    {
        InitializeComponent();

        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebViewTroubleshooter",
            "LegacyWebView.BugRepro",
            "Logs");
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, $"legacy-webview-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        _scriptBridge = new WebViewScriptBridge(this);
        Browser.ObjectForScripting = _scriptBridge;
        Browser.ScriptErrorsSuppressed = true;

        Browser.Navigating += Browser_Navigating;
        Browser.Navigated += Browser_Navigated;
        Browser.LoadCompleted += Browser_LoadCompleted;

        Loaded += (_, _) => InitializeBrowser();
    }

    private void InitializeBrowser()
    {
        Log($"App started. Log file: {_logFilePath}");
        Log("Using legacy WPF WebBrowser (IE/Trident engine). IE11 emulation registry flag applied.");

        var startUrl = GetArgumentValue("--url")
            ?? new Uri(Path.Combine(AppContext.BaseDirectory, "test-page.html")).AbsoluteUri;
        Navigate(startUrl);
    }

    private void Browser_Navigating(object? sender, NavigatingCancelEventArgs e)
    {
        Log($"Navigating: {e.Uri}");
    }

    private void Browser_Navigated(object? sender, NavigationEventArgs e)
    {
        Log($"Navigated: {e.Uri}");
        AddressBar.Text = Browser.Source?.ToString() ?? string.Empty;
        BackButton.IsEnabled = Browser.CanGoBack;
        ForwardButton.IsEnabled = Browser.CanGoForward;
    }

    private void Browser_LoadCompleted(object? sender, NavigationEventArgs e)
    {
        Log($"LoadCompleted: {e.Uri}");
        InjectConsoleHook();
    }

    private void InjectConsoleHook()
    {
        try
        {
            dynamic document = Browser.Document;
            dynamic? head = document?.GetElementsByTagName("head")?[0];
            if (head is null)
            {
                return;
            }

            dynamic scriptElement = document.createElement("script");
            scriptElement.type = "text/javascript";
            scriptElement.text = ConsoleHookScript;
            head.appendChild(scriptElement);
        }
        catch (Exception ex)
        {
            Log($"Console hook injection failed: {ex.Message}");
        }
    }

    private static string? GetArgumentValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..].Trim('"');
            }
        }
        return null;
    }

    private void Navigate(string rawInput)
    {
        var url = rawInput.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        Log($"Navigate requested: {url}");
        Browser.Navigate(url);
    }

    private void GoButton_Click(object sender, RoutedEventArgs e) => Navigate(AddressBar.Text);

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Navigate(AddressBar.Text);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoBack) Browser.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoForward) Browser.GoForward();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => Browser.Refresh();

    private void DevToolsButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Legacy WebBrowser has no built-in DevTools. Press F12 while the browser has focus if IE Developer Tools are installed, or attach a debugger to the iexplore process hosting the control.",
            "DevTools",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LocalTestButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-page.html");
        Browser.Navigate(new Uri(path).AbsoluteUri);
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Close the app, then clear IE browsing data (Control Panel > Internet Options > Delete) or delete %LOCALAPPDATA%\\Microsoft\\Windows\\INetCache. Legacy WebBrowser shares the IE cache profile.",
            "Clear cache",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CopyLogButton_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(LogBox.Text);

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _logDirectory,
            UseShellExecute = true
        });
    }

    public void LogFromScript(string message) => Log(message);

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:O}] {message}";
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
        File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
    }
}

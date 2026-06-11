using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace WebView2.Working;

public partial class MainWindow : Window
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly string _userDataFolder;

    public MainWindow()
    {
        InitializeComponent();

        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebViewTroubleshooter",
            "WebView2.Working",
            "Logs");
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, $"webview-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        _userDataFolder = GetArgumentValue("--userDataDir")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebViewTroubleshooter", "WebView2.Working", "Profile");

        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(_userDataFolder);

            Log($"App started. Log file: {_logFilePath}");
            Log($"WebView2 user data folder: {_userDataFolder}");

            var environmentOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--enable-logging --v=1"
            };

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _userDataFolder,
                options: environmentOptions);

            await Browser.EnsureCoreWebView2Async(environment);

            Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            Browser.CoreWebView2.Settings.IsScriptEnabled = true;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.IsWebMessageEnabled = true;

            Browser.CoreWebView2.NavigationStarting += (_, e) => Log($"NavigationStarting: {e.Uri}");
            Browser.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                Log($"NavigationCompleted: success={e.IsSuccess}, status={e.HttpStatusCode}, error={e.WebErrorStatus}");
                BackButton.IsEnabled = Browser.CanGoBack;
                ForwardButton.IsEnabled = Browser.CanGoForward;
                AddressBar.Text = Browser.Source?.ToString() ?? string.Empty;
            };
            Browser.CoreWebView2.SourceChanged += (_, _) => AddressBar.Text = Browser.Source?.ToString() ?? string.Empty;
            Browser.CoreWebView2.WebMessageReceived += (_, e) => Log($"WebMessageReceived: {e.TryGetWebMessageAsString()}");
            Browser.CoreWebView2.ProcessFailed += (_, e) => Log($"ProcessFailed: kind={e.ProcessFailedKind}, reason={e.Reason}, exitCode={e.ExitCode}");
            await EnableConsoleCaptureAsync(Browser.CoreWebView2);
            Browser.CoreWebView2.DOMContentLoaded += (_, e) => Log($"DOMContentLoaded: navigationId={e.NavigationId}");

            var startUrl = GetArgumentValue("--url")
                ?? new Uri(Path.Combine(AppContext.BaseDirectory, "test-page.html")).AbsoluteUri;
            Navigate(startUrl);
        }
        catch (Exception ex)
        {
            Log("Initialization failed: " + ex);
            MessageBox.Show(ex.ToString(), "WebView2 initialization failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task EnableConsoleCaptureAsync(CoreWebView2 coreWebView2)
    {
        var receiver = coreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
        receiver.DevToolsProtocolEventReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                var level = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "log" : "log";
                var message = FormatConsoleArgs(root);
                var source = string.Empty;
                var lineNumber = 0;

                if (root.TryGetProperty("stackTrace", out var stackTrace)
                    && stackTrace.TryGetProperty("callFrames", out var frames)
                    && frames.GetArrayLength() > 0)
                {
                    var frame = frames[0];
                    source = frame.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty;
                    lineNumber = frame.TryGetProperty("lineNumber", out var line) ? line.GetInt32() + 1 : 0;
                }

                Log($"Console[{level}] {source}:{lineNumber} - {message}");
            }
            catch
            {
                Log($"Console: {e.ParameterObjectAsJson}");
            }
        };

        await coreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
    }

    private static string FormatConsoleArgs(JsonElement root)
    {
        if (!root.TryGetProperty("args", out var args))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var arg in args.EnumerateArray())
        {
            if (arg.TryGetProperty("value", out var value))
            {
                parts.Add(value.ToString());
            }
            else if (arg.TryGetProperty("description", out var description))
            {
                parts.Add(description.GetString() ?? string.Empty);
            }
            else
            {
                parts.Add(arg.GetRawText());
            }
        }

        return string.Join(" ", parts);
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
        if (Browser.CoreWebView2 is null)
        {
            Log("Navigate skipped because WebView2 is not initialized yet.");
            return;
        }

        var url = rawInput.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        Log($"Navigate requested: {url}");
        Browser.CoreWebView2.Navigate(url);
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

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => Browser.Reload();

    private void DevToolsButton_Click(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.OpenDevToolsWindow();

    private void LocalTestButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-page.html");
        Browser.CoreWebView2?.Navigate(new Uri(path).AbsoluteUri);
    }

    private void ClearProfileButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Close the app, delete the Profile folder under %LOCALAPPDATA%\\WebViewTroubleshooter\\WebView2.Working, then reopen. The folder cannot be safely deleted while WebView2 is running.",
            "Clear profile",
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

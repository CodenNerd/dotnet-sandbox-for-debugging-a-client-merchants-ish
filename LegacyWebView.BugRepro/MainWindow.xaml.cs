using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace LegacyWebView.BugRepro;

public partial class MainWindow : Window
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly WebViewScriptBridge _scriptBridge;
    private LocalPageServer? _localPageServer;
    private DispatcherTimer? _diagnosticsTimer;

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

        Browser.Navigating += Browser_Navigating;
        Browser.Navigated += Browser_Navigated;
        Browser.LoadCompleted += Browser_LoadCompleted;
        Closed += (_, _) =>
        {
            _diagnosticsTimer?.Stop();
            _localPageServer?.Dispose();
        };

        Loaded += (_, _) => InitializeBrowser();
    }

    private void InitializeBrowser()
    {
        WebBrowserHelper.SuppressScriptErrors(Browser, true);

        Log($"App started. Log file: {_logFilePath}");
        Log($"Content root: {AppContext.BaseDirectory}");
        Log("Using legacy WPF WebBrowser (IE/Trident engine). IE11 emulation registry flag applied.");

        var explicitUrl = GetArgumentValue("--url");
        if (explicitUrl is not null)
        {
            Navigate(explicitUrl);
            return;
        }

        try
        {
            _localPageServer = LocalPageServer.Start(AppContext.BaseDirectory, Log);
            Log($"Serving test page over HTTP at {_localPageServer.BaseUrl} (avoids file:// script restrictions).");
            Navigate(_localPageServer.BaseUrl + "test-page.html");
        }
        catch (Exception ex)
        {
            ReportDiagnostic("error", "Failed to start local HTTP server: " + ex.Message);
            Log("Initialization failed: " + ex);
        }
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
        WebBrowserHelper.SuppressScriptErrors(Browser, true);
        Log($"LoadCompleted: {e.Uri}");

        if (Browser.Document is null)
        {
            ReportDiagnostic("error", "Load completed but Browser.Document is null.");
            return;
        }

        ScheduleDiagnostics("initial");
    }

    private void ScheduleDiagnostics(string reason)
    {
        _diagnosticsTimer?.Stop();
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _diagnosticsTimer.Tick += (_, _) =>
        {
            _diagnosticsTimer.Stop();
            RunDiagnostics(reason);
        };
        _diagnosticsTimer.Start();
    }

    private void RunDiagnostics(string reason)
    {
        Log($"--- Diagnostics ({reason}) ---");

        try
        {
            dynamic? document = Browser.Document;
            if (document is null)
            {
                ReportDiagnostic("error", "Document is null during diagnostics.");
                Log("Diagnostic: document is null");
                return;
            }

            dynamic? window = document.parentWindow;
            string userAgent = window?.navigator?.userAgent ?? "unknown";
            Log($"User-Agent: {userAgent}");

            bool externalAvailable = false;
            try
            {
                externalAvailable = window?.external is not null;
            }
            catch
            {
            }
            Log($"window.external available: {externalAvailable}");

            string? readyState = document.readyState;
            Log($"document.readyState: {readyState}");

            string? pageStatus = TryEval("window.__pageStatus || 'not set'");
            Log($"window.__pageStatus: {pageStatus}");

            string? staxLoadState = TryEval("window.__staxLoadState || 'not set'");
            Log($"window.__staxLoadState: {staxLoadState}");

            string fattJsType = TryEval("typeof FattJs") ?? "unknown";
            Log($"typeof FattJs: {fattJsType}");

            string fattjsType = TryEval("typeof fattjs") ?? "unknown";
            Log($"typeof fattjs: {fattjsType}");

            string? lastError = TryEval("window.__lastScriptError || 'none'");
            Log($"window.__lastScriptError: {lastError}");

            if (fattJsType == "undefined")
            {
                ReportDiagnostic("error", $"stax.js did not load (typeof FattJs=undefined). State={staxLoadState}. See log for details.");
            }
            else if (staxLoadState == "initialized")
            {
                ReportDiagnostic("info", "stax.js loaded and FattJs initialized.");
            }
            else if (staxLoadState == "init-failed")
            {
                ReportDiagnostic("error", "stax.js loaded but FattJs initialization failed. See log.");
            }
            else
            {
                ReportDiagnostic("warn", $"Page loaded but stax state is '{staxLoadState}'. Check log.");
            }
        }
        catch (Exception ex)
        {
            ReportDiagnostic("error", "Diagnostics failed: " + ex.Message);
            Log("Diagnostic exception: " + ex);
        }

        Log("--- End diagnostics ---");
    }

    private string? TryEval(string expression)
    {
        try
        {
            dynamic? document = Browser.Document;
            if (document is null)
            {
                return null;
            }

            dynamic? window = document.parentWindow;
            dynamic? result = window?.eval(expression);
            return result?.ToString();
        }
        catch (Exception ex)
        {
            Log($"eval({expression}) failed: {ex.Message}");
            return null;
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

    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e) => RunDiagnostics("manual");

    private void LocalTestButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _localPageServer ??= LocalPageServer.Start(AppContext.BaseDirectory, Log);
            Navigate(_localPageServer.BaseUrl + "test-page.html");
        }
        catch (Exception ex)
        {
            ReportDiagnostic("error", "Failed to start local HTTP server: " + ex.Message);
            Log("Local test failed: " + ex);
        }
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

    public void ReportDiagnostic(string level, string message)
    {
        Log($"[{level.ToUpperInvariant()}] {message}");
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            StatusText.Foreground = level switch
            {
                "error" => System.Windows.Media.Brushes.DarkRed,
                "warn" => System.Windows.Media.Brushes.DarkOrange,
                _ => System.Windows.Media.Brushes.DarkGreen
            };
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

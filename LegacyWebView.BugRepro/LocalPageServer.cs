using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace LegacyWebView.BugRepro;

internal sealed class LocalPageServer : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> ProxiedScripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["vendor/es6-promise.auto.min.js"] = "https://cdnjs.cloudflare.com/ajax/libs/es6-promise/4.2.8/es6-promise.auto.min.js",
        ["vendor/staxjs-captcha.js"] = "https://staxjs.staxpayments.com/staxjs-captcha.js"
    };

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _root;
    private readonly Task _loop;
    private readonly Action<string>? _requestLogger;
    private readonly ConcurrentDictionary<string, byte[]> _proxyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient = new();

    public string BaseUrl { get; }

    private LocalPageServer(HttpListener listener, int port, string root, Action<string>? requestLogger)
    {
        _listener = listener;
        _root = root;
        _requestLogger = requestLogger;
        BaseUrl = $"http://127.0.0.1:{port}/";
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public static LocalPageServer Start(string contentRoot, Action<string>? requestLogger = null)
    {
        var root = Path.GetFullPath(contentRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        for (var port = 8765; port < 8865; port++)
        {
            if (!IsPortAvailable(port))
            {
                continue;
            }

            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                return new LocalPageServer(listener, port, root, requestLogger);
            }
            catch (HttpListenerException)
            {
            }
        }

        throw new InvalidOperationException("Could not start a local HTTP server for the test page.");
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(token);
                _ = Task.Run(() => HandleRequest(context), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (token.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var relativePath = context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
        if (string.IsNullOrEmpty(relativePath))
        {
            relativePath = "test-page.html";
        }

        try
        {
            if (ProxiedScripts.TryGetValue(relativePath, out var upstreamUrl))
            {
                ServeProxiedScript(context, relativePath, upstreamUrl);
                return;
            }

            var filePath = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!filePath.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                _requestLogger?.Invoke($"HTTP 404: GET /{relativePath}");
                return;
            }

            var bytes = File.ReadAllBytes(filePath);
            WriteResponse(context, 200, GetContentType(filePath), bytes);
            _requestLogger?.Invoke($"HTTP 200: GET /{relativePath} ({bytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            _requestLogger?.Invoke($"HTTP 500: GET /{relativePath} - {ex.Message}");
        }
        finally
        {
            context.Response.Close();
        }
    }

    private void ServeProxiedScript(HttpListenerContext context, string relativePath, string upstreamUrl)
    {
        var bytes = _proxyCache.GetOrAdd(relativePath, _ => DownloadScript(upstreamUrl));
        WriteResponse(context, 200, "application/javascript; charset=utf-8", bytes);
        _requestLogger?.Invoke($"HTTP 200: GET /{relativePath} (proxied, {bytes.Length} bytes) <- {upstreamUrl}");
    }

    private byte[] DownloadScript(string upstreamUrl)
    {
        _requestLogger?.Invoke($"Proxy fetch: {upstreamUrl}");
        using var response = _httpClient.GetAsync(upstreamUrl).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    private static void WriteResponse(HttpListenerContext context, int statusCode, string contentType, byte[] bytes)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static string GetContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".htm" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };

    public void Dispose()
    {
        _cts.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }
        _listener.Close();
        _httpClient.Dispose();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _cts.Dispose();
    }
}

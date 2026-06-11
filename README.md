# WebViewTroubleshooter

A tiny WPF + WebView2 desktop app for reproducing customer WebView issues.

## Prerequisites

- Windows
- Visual Studio 2022 or the .NET SDK with WPF support
- Microsoft Edge WebView2 Runtime installed on the machine

## Run

```powershell
dotnet restore
dotnet run -- --url=https://www.example.com
```

You can pass a customer URL:

```powershell
dotnet run -- --url=https://customer-site.example/path
```

You can isolate customer repros by using a separate WebView2 profile folder:

```powershell
dotnet run -- --url=https://customer-site.example --userDataDir=C:\Temp\WebView2CustomerProfile
```

## What it logs

The app writes a diagnostic log to:

```text
%LOCALAPPDATA%\WebViewTroubleshooter\Logs
```

It records:

- Navigation start/completion
- HTTP status when available
- WebView2 web error status
- Console messages from the page
- JavaScript messages sent with `chrome.webview.postMessage(...)`
- WebView2 process failures

## Useful buttons

- **Local Test**: loads `test-page.html` from the output directory.
- **DevTools**: opens Chromium DevTools for the embedded WebView.
- **Clear Profile**: shows where to delete the profile folder after closing the app.
- **Open Log Folder**: opens the folder containing the diagnostic log.

## Notes

This sample uses a dedicated WebView2 user data folder by default so cookies, cache, and local storage are isolated from Edge and from other repro runs.

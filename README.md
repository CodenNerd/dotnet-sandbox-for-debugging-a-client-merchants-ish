# WebViewTroubleshooter

A WPF sandbox for reproducing merchant WebView payment issues (PHO-5058 / Hindsite).

The solution contains two apps:

| Project | Purpose | Browser engine |
| --- | --- | --- |
| **WebView2.Working** | Known-good reference implementation | WebView2 (Chromium / Edge) |
| **LegacyWebView.BugRepro** | Reproduces legacy integration problems | WPF `WebBrowser` (IE / Trident) |

## Prerequisites

- Windows
- Visual Studio 2022 or the .NET SDK with WPF support
- **WebView2.Working only:** Microsoft Edge WebView2 Runtime

## Run

```powershell
dotnet restore
```

### Working version (WebView2)

```powershell
dotnet run --project WebView2.Working
```

Uses `card_form_incomplete` (current stax.js event name). Loads `test-page.html` by default.

```powershell
dotnet run --project WebView2.Working -- --url=https://customer-site.example/path
```

### Bug repro version (legacy WebView)

```powershell
dotnet run --project LegacyWebView.BugRepro
```

Uses the deprecated `card_form_uncomplete` event and the legacy WPF `WebBrowser` control with IE11 emulation. Loads `test-page.html` by default over `http://127.0.0.1` (avoids IE `file://` script restrictions) and suppresses script error dialogs in the log panel instead.

```powershell
dotnet run --project LegacyWebView.BugRepro -- --url=https://customer-site.example/path
```

Isolate profiles with a separate user data folder (WebView2 only):

```powershell
dotnet run --project WebView2.Working -- --userDataDir=C:\Temp\WebView2CustomerProfile
```

## What it logs

Logs are written under `%LOCALAPPDATA%\WebViewTroubleshooter\`:

- `WebView2.Working\Logs`
- `LegacyWebView.BugRepro\Logs`

Both apps record:

- Navigation start / completion
- Console messages from the page
- Host messages (`chrome.webview.postMessage` on WebView2; shimmed via `window.external` on legacy WebView)
- WebView2 process failures (WebView2.Working only)

## Useful buttons

- **Local Test** — reloads `test-page.html` from the output directory
- **DevTools** — Chromium DevTools (WebView2) or guidance for IE tools (legacy)
- **Clear Profile / Clear Cache** — where to reset browser state after closing the app
- **Open Log Folder** — opens the diagnostic log directory

## Notes

- **WebView2.Working** uses a dedicated WebView2 profile folder so cookies, cache, and local storage are isolated from Edge.
- **LegacyWebView.BugRepro** sets the IE11 browser emulation registry flag so the `WebBrowser` control uses a modern document mode where possible. It still uses the legacy Trident engine and may behave differently from WebView2 or a normal browser.
- stax.js may not fully work in the legacy WebBrowser control; that mismatch is part of what this repro is meant to surface.
- **LegacyWebView.BugRepro** trusts `localhost` / `127.0.0.1` in the IE zone map, disables local-machine lockdown for the process, and proxies `staxjs-captcha.js` through the local HTTP server so scripts load same-origin. If `FattJs` is still undefined after download, the script is not compatible with the IE engine — Stax only supports modern browsers. Use **WebView2.Working** for a working tokenize flow.

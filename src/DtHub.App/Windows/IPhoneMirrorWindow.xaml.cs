using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

using DtHub.App.Services;
using DtHub.Core.IPhone;
using DtHub.Core.Localization;
using DtHub.Core.Processes;
using DtHub.Infrastructure.IPhone;

using Microsoft.Web.WebView2.Core;

using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// One iPhone's screen: pymobiledevice3's viewer page, in DT Hub's web
/// engine, with only the screen left. The page sends clicks and keys back
/// to the iPhone as touches. Closing the window ends the stream.
/// </summary>
public partial class IPhoneMirrorWindow : Window
{
    /// <summary>
    /// Runs before the viewer's own script: frame off, no zoom, whole
    /// decoded frames instead of Edge's 1280x720 crop, then its
    /// bars, side panels and buttons hidden. Its canvas sizing stays its
    /// own, so a click still lands where the page expects. Also says when
    /// the engine cannot decode the stream (HEVC, in hardware on most PCs).
    /// </summary>
    private const string ScreenOnly = """
        try { localStorage.setItem('frameOn', 'false'); localStorage.setItem('zoom', '1'); } catch (e) {}
        // Without a size in its config, as the viewer sets it, Edge hands each
        // frame cropped to 1280x720: the top of the screen only (measured
        // 2026-11-09). The whole picture is decoded, so show all of it.
        // shortcut: the coded size may hold a few alignment pixels past the picture, unseen at 1264x2752.
        if (typeof VideoDecoder !== 'undefined') {
          window.VideoDecoder = class extends VideoDecoder {
            constructor(init) {
              super({ ...init, output: f => {
                const w = f.codedWidth, h = f.codedHeight;
                if (f.visibleRect.width === w && f.visibleRect.height === h && f.displayWidth === w && f.displayHeight === h) {
                  init.output(f);
                  return;
                }
                let whole;
                try { whole = new VideoFrame(f, { visibleRect: { x: 0, y: 0, width: w, height: h }, displayWidth: w, displayHeight: h }); }
                finally { f.close(); }
                init.output(whole);
              } });
            }
          };
        }
        // The viewer makes a right click the Home button: the game went to the
        // background where a DT Hub player expects scrcpy's Back.
        window.addEventListener('contextmenu', e => { e.preventDefault(); e.stopImmediatePropagation(); }, true);
        // '?' opens the viewer's help, hidden here, which then swallows every key
        // until Esc: "ça va ?" in the chat froze the keyboard. Sent as a key instead.
        window.addEventListener('keydown', e => { if (e.key === '?') Object.defineProperty(e, 'key', { value: '' }); }, true);
        document.addEventListener('DOMContentLoaded', () => {
          const style = document.createElement('style');
          style.textContent = '#topbar,#left-tray,#right-tray,#bottom-row,#hw-tooltip,#help-overlay,.hw,#stage::before'
            + '{display:none!important}html,body{background:#000!important;overflow:hidden}'
            // The viewer keeps room for its bars, hidden here: the screen fills the window instead.
            // Clicks stay right, the viewer maps them from the canvas's on-screen box.
            + 'body,#stage-wrap{padding:0!important;gap:0!important}#stage-wrap{justify-content:center}'
            + '#c{width:auto!important;height:auto!important;max-width:100vw;max-height:100vh}';
          document.head.appendChild(style);
          // The viewer's Rotate is in a hidden bar: DOFUS Touch is landscape inside a portrait
          // picture until the iPhone turns. A small button over the screen does it.
          const turn = document.createElement('button');
          turn.textContent = '\u21bb';
          turn.title = __ROTATE__;
          turn.style.cssText = 'position:fixed;top:6px;right:6px;z-index:9;width:28px;height:28px;padding:0;border:0;'
            + 'border-radius:14px;background:rgba(0,0,0,.55);color:#fff;font-size:16px;cursor:pointer;opacity:.4';
          turn.onmouseenter = () => turn.style.opacity = '1';
          turn.onmouseleave = () => turn.style.opacity = '.4';
          turn.onclick = () => document.getElementById('rotate-right').click();
          document.body.appendChild(turn);
          // The window takes the picture's shape, so no band is left around it.
          let shape = '';
          setInterval(() => {
            const c = document.getElementById('c');
            const now = c && c.width ? c.width + 'x' + c.height : '';
            if (now && now !== shape) { shape = now; chrome.webview.postMessage('shape:' + now); }
          }, 300);
          (typeof VideoDecoder === 'undefined'
            ? Promise.resolve(false)
            : VideoDecoder.isConfigSupported({ codec: 'hvc1.1.6.L93.B0' }).then(s => s.supported, () => false))
            .then(ok => { if (!ok) chrome.webview.postMessage('no-hevc'); });
        });
        """;

    // The first stream can wait for Python to load and the tunnel to open.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(90);

    private readonly IProcessSession _session;
    private readonly WebViewEnvironment _engine;
    private readonly Uri _address;
    private readonly StringBuilder _errors = new();

    private bool _closed;

    // Set once a message replaced the screen: the page finishing to load
    // must not cover it with a black canvas.
    private bool _failed;

    /// <summary>The picture's width over its height, 0 until the first frame.</summary>
    public double Aspect { get; private set; }

    public nint Handle => new WindowInteropHelper(this).Handle;

    public IPhoneMirrorWindow(IPhoneDevice device, int port, IProcessSession session, WebViewEnvironment engine)
    {
        ArgumentNullException.ThrowIfNull(device);

        _session = session;
        _engine = engine;
        _address = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");

        InitializeComponent();

        Title = device.Name;
        Status.Text = Strings.Format("IPhoneStarting", device.Name);

        Loaded += async (_, _) => await ShowScreenAsync().ConfigureAwait(true);
        Closed += async (_, _) =>
        {
            _closed = true;
            View.Dispose();
            await _session.DisposeAsync().ConfigureAwait(true);
        };
    }

    private async Task ShowScreenAsync()
    {
        _ = WatchAsync();

        try
        {
            await View.EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true)).ConfigureAwait(true);
            await View.CoreWebView2
                .AddScriptToExecuteOnDocumentCreatedAsync(ScreenOnly.Replace("__ROTATE__", JsonSerializer.Serialize(Strings.Get("IPhoneRotate")), StringComparison.Ordinal))
                .ConfigureAwait(true);
            View.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            View.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                var message = e.TryGetWebMessageAsString();

                if (message == "no-hevc")
                {
                    ShowStatus(Strings.Get("IPhoneNoHevc"));
                }
                else if (message.StartsWith("shape:", StringComparison.Ordinal))
                {
                    FitTo(message["shape:".Length..]);
                }
            };

            if (!await WaitUntilServedAsync().ConfigureAwait(true))
            {
                return;
            }

            var loaded = new TaskCompletionSource<bool>();
            View.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            View.CoreWebView2.Navigate(_address.ToString());

            if (!await loaded.Task.ConfigureAwait(true))
            {
                ShowStatus(Strings.Get("IPhoneFailed"));
                return;
            }

            if (_failed)
            {
                return;
            }

            View.Visibility = Visibility.Visible;
            Status.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Also a window closed while the engine was starting.
            Log.Warning(exception, "L'écran de l'iPhone n'a pas pu s'afficher.");
            ShowStatus(exception switch
            {
                // Its message says how to install it.
                WebView2RuntimeNotFoundException => exception.Message,

                // msedgewebview2.exe gone, the runtime still registered (2026-11-09).
                FileNotFoundException => Strings.Get("IPhoneWebViewBroken"),
                _ => Strings.Get("IPhoneFailed"),
            });
        }
    }

    /// <summary>
    /// Gives the window the picture's shape, its long side kept: no black
    /// band around the screen, and a landscape game gets a landscape window.
    /// </summary>
    private void FitTo(string shape)
    {
        var parts = shape.Split('x');

        if (parts.Length != 2
            || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var height)
            || width <= 0 || height <= 0)
        {
            return;
        }

        Aspect = width / height;

        if (WindowState != WindowState.Normal || View.ActualWidth <= 0 || View.ActualHeight <= 0)
        {
            return;
        }

        var chromeWidth = ActualWidth - View.ActualWidth;
        var chromeHeight = ActualHeight - View.ActualHeight;
        var side = Math.Max(View.ActualWidth, View.ActualHeight);

        Width = (Aspect >= 1 ? side : side * Aspect) + chromeWidth;
        Height = (Aspect >= 1 ? side / Aspect : side) + chromeHeight;
    }

    /// <summary>Polls the viewer page until it answers, or the tool stops.</summary>
    private async Task<bool> WaitUntilServedAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + ReadyTimeout;

        while (!_closed && !_session.HasExited && DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync(_address).ConfigureAwait(true);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // The two-second request timeout: try again.
            }

            await Task.Delay(500).ConfigureAwait(true);
        }

        // A stopped tool is explained by WatchAsync.
        if (!_closed && !_session.HasExited)
        {
            ShowStatus(Strings.Get("IPhoneFailed"));
        }

        return false;
    }

    /// <summary>
    /// Keeps the tool's errors, and says why when it stops: an iPhone
    /// unplugged or locked ends the stream. Closing the window ends the
    /// output too.
    /// </summary>
    private async Task WatchAsync()
    {
        await foreach (var line in _session.Output.ReadAllAsync().ConfigureAwait(true))
        {
            if (line.IsError)
            {
                _errors.AppendLine(line.Text);
            }
        }

        if (_closed)
        {
            return;
        }

        Log.Warning("L'affichage de l'iPhone s'est arrêté : {Errors}", _errors.ToString());
        ShowStatus(Strings.Format("IPhoneStopped", PyMobileDevice.Explain(_errors.ToString())));
    }

    private void ShowStatus(string text)
    {
        if (_closed)
        {
            return;
        }

        _failed = true;
        View.Visibility = Visibility.Hidden;
        Status.Visibility = Visibility.Visible;
        Status.Text = text;
    }
}

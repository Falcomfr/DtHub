using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;

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
                const whole = new VideoFrame(f, { visibleRect: { x: 0, y: 0, width: w, height: h }, displayWidth: w, displayHeight: h });
                f.close();
                init.output(whole);
              } });
            }
          };
        }
        document.addEventListener('DOMContentLoaded', () => {
          const style = document.createElement('style');
          style.textContent = '#topbar,#left-tray,#right-tray,#bottom-row,#hw-tooltip,#help-overlay,.hw,#stage::before'
            + '{display:none!important}html,body{background:#000!important;overflow:hidden}';
          document.head.appendChild(style);
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
            await View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ScreenOnly).ConfigureAwait(true);
            View.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            View.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (e.TryGetWebMessageAsString() == "no-hevc")
                {
                    ShowStatus(Strings.Get("IPhoneNoHevc"));
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

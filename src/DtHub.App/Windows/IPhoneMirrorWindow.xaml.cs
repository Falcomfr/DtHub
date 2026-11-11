using System.Globalization;
using System.IO;
using System.Net.Http;
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
        // The viewer's Ctrl+H, L, S, [, ], \ press Home, Lock (which ends the stream), Siri and the
        // volume, Ctrl+P saves a screenshot, Ctrl+=, - and 0 zoom: DT Hub players use Ctrl shortcuts.
        window.addEventListener('keydown', e => {
          if (e.ctrlKey && !e.altKey && !e.metaKey && e.key.length === 1 && 'hls[]\\p=+-0'.includes(e.key.toLowerCase())) {
            e.preventDefault();
            e.stopImmediatePropagation();
          }
        }, true);
        // AltGr is Ctrl+Alt on Windows: the viewer sent Ctrl+Option and the key, so @ # { } never came out.
        // The character goes through the iPhone's clipboard and Cmd+V instead, whatever its keyboard layout.
        window.addEventListener('keydown', e => {
          if (!e.getModifierState('AltGraph') || e.key.length !== 1 || document.activeElement !== document.getElementById('c')) return;
          e.preventDefault();
          e.stopImmediatePropagation();
          if (e.repeat) return;
          const post = (path, body) => () => fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
          // iOS ignores a modifier that arrives with its key: Cmd goes down first, as the tool's own typing does.
          const wait = () => new Promise(r => setTimeout(r, 40));
          lastKeyPost = lastKeyPost.then(post('/clipboard', { text: e.key }))
            .then(post('/key', { usages: [0xE3] })).then(wait)
            .then(post('/key', { usages: [0xE3, 0x19] })).then(wait)
            .then(post('/key', { usages: [] })).catch(() => {});
        }, true);
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
            + '#c{width:auto!important;height:auto!important;max-width:100vw;max-height:100vh}'
            + '#offline-overlay span{white-space:normal!important;max-width:80%;text-align:center}';
          document.head.appendChild(style);
          // Shown when the frames stop, most often an iPhone that locked itself.
          document.querySelector('#offline-overlay span').textContent = __OFFLINE__;
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
          // iOS cannot switch its screen off while streaming it: "turn the screen
          // off" sets the brightness to its least instead, as the brightness keys
          // of a keyboard do. Sixteen presses go from any level to either end.
          const moon = turn.cloneNode();
          moon.style.right = '40px';
          moon.textContent = '\u263e';
          moon.title = __DIM__;
          moon.onmouseenter = () => moon.style.opacity = '1';
          moon.onmouseleave = () => moon.style.opacity = '.4';
          let dark = false, pressing = Promise.resolve();
          window.dthubDark = on => {
            on = on === undefined ? !dark : on;
            if (on === dark) return;
            dark = on;
            moon.textContent = on ? '\u2600' : '\u263e';
            const name = on ? 'brightness-down' : 'brightness-up';
            for (let i = 0; i < 16; i++) {
              pressing = pressing.then(() => fetch('/button', { method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, state: 'press' }) }), () => {});
            }
          };
          moon.onclick = () => window.dthubDark();
          document.body.appendChild(moon);
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
    // The tool's last error lines: Explain reads only the last one.
    private readonly Queue<string> _errors = new();

    private readonly bool _dark;
    private int _logged;

    private bool _closed;

    // Set once a message replaced the screen: the page finishing to load
    // must not cover it with a black canvas.
    private bool _failed;

    /// <summary>The iPhone left the USB list or its tunnel: the tool is stopped, the window waits for it.</summary>
    public bool IsUnplugged { get; private set; }

    /// <summary>The picture's width over its height, 0 until the first frame.</summary>
    public double Aspect { get; private set; }

    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Held in DT Hub's tabbed frame: the frame sizes it, so the picture's shape is only told.</summary>
    public bool Docked { get; set; }

    /// <summary>The picture changed shape: a first frame, or the device turned.</summary>
    public event EventHandler? AspectChanged;

    /// <summary>The viewer page answers: the stream serves.</summary>
    public event EventHandler? Served;

    /// <summary>The stream stopped or never served, not on an unplug: a failure.</summary>
    public event EventHandler? Stopped;

    // Read by the foreground watch, off the UI thread, as QuestPageWindow's:
    // unknown, this window put the shortcuts to sleep and Ctrl+E reached the
    // iPhone as a plain key, never dimming it (2026-11-11).
    private static readonly HashSet<nint> Handles = [];

    /// <summary>True if this handle is that of an open iPhone window.</summary>
    public static bool Owns(nint handle)
    {
        lock (Handles)
        {
            return Handles.Contains(handle);
        }
    }

    public IPhoneMirrorWindow(IPhoneDevice device, int port, IProcessSession session, WebViewEnvironment engine, bool dark)
    {
        ArgumentNullException.ThrowIfNull(device);

        _dark = dark;
        _session = session;
        _engine = engine;
        _address = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");

        InitializeComponent();

        Title = device.Name;
        Status.Text = Strings.Format("IPhoneStarting", device.Name);

        nint own = 0;
        SourceInitialized += (_, _) =>
        {
            own = Handle;
            lock (Handles)
            {
                Handles.Add(own);
            }
        };

        Loaded += async (_, _) => await ShowScreenAsync().ConfigureAwait(true);
        // A finger or key held while the window loses the keyboard stays down on the
        // iPhone unless the page sees it go: the viewer releases everything on blur.
        Deactivated += (_, _) => SendBlur();
        Closed += async (_, _) =>
        {
            lock (Handles)
            {
                Handles.Remove(own);
            }

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
                .AddScriptToExecuteOnDocumentCreatedAsync(ScreenOnly
                    .Replace("__ROTATE__", JsonSerializer.Serialize(Strings.Get("IPhoneRotate")), StringComparison.Ordinal)
                    .Replace("__DIM__", JsonSerializer.Serialize(Strings.Get("IPhoneDim")), StringComparison.Ordinal)
                    .Replace("__OFFLINE__", JsonSerializer.Serialize(Strings.Get("IPhoneOffline")), StringComparison.Ordinal))
                .ConfigureAwait(true);
            View.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

            // F5 or Ctrl+R reloaded the page and lost its sound, Ctrl+wheel zoomed it.
            View.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            View.CoreWebView2.Settings.IsZoomControlEnabled = false;
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

            Served?.Invoke(this, EventArgs.Empty);

            var loaded = new TaskCompletionSource<bool>();
            View.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            View.CoreWebView2.Navigate(_address.ToString());

            if (!await loaded.Task.ConfigureAwait(true))
            {
                ShowStatus(Strings.Get("IPhoneFailed"));

                // Unplugged meanwhile: the page went with the tool, no failure.
                if (!_closed && !IsUnplugged)
                {
                    Stopped?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            if (_failed)
            {
                return;
            }

            View.Visibility = Visibility.Visible;
            Status.Visibility = Visibility.Collapsed;

            if (_dark)
            {
                SetDark(true);
            }
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
    /// Stops the tool of an unplugged iPhone: it only waits for a tunnel
    /// service DT Hub does not run, the picture frozen. Opened again once
    /// the iPhone is back (<see cref="IPhoneScreens"/>).
    /// </summary>
    public void Unplugged()
    {
        if (IsUnplugged || _closed)
        {
            return;
        }

        IsUnplugged = true;
        _session.Kill();
    }

    /// <summary>Releases what the page holds down, as when the window loses the keyboard.</summary>
    public void SendBlur()
    {
        if (!_closed)
        {
            _ = View.CoreWebView2?.ExecuteScriptAsync("dispatchEvent(new Event('blur'))");
        }
    }

    /// <summary>Dims the iPhone, lights it, or with null turns it the other way.</summary>
    public void SetDark(bool? dark)
    {
        var argument = dark switch { true => "true", false => "false", null => string.Empty };
        _ = View.CoreWebView2?.ExecuteScriptAsync($"window.dthubDark && window.dthubDark({argument})");
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

        if (Aspect != width / height)
        {
            Aspect = width / height;
            AspectChanged?.Invoke(this, EventArgs.Empty);
        }

        if (Docked || WindowState != WindowState.Normal || View.ActualWidth <= 0 || View.ActualHeight <= 0)
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
            Stopped?.Invoke(this, EventArgs.Empty);
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
            // Read when a player sends the log: what the iPhone said, not only why it stopped.
            if (_logged++ < 300)
            {
                Log.Information("Outil iPhone : {Line}", line.Text);
            }

            // The tool's own reconnection waits for tunneld, which never comes here.
            if (line.Text.Contains("waiting for tunneld", StringComparison.Ordinal))
            {
                Unplugged();
            }

            if (line.IsError)
            {
                _errors.Enqueue(line.Text);
                if (_errors.Count > 50)
                {
                    _errors.Dequeue();
                }
            }
        }

        if (_closed)
        {
            return;
        }

        var errors = string.Join('\n', _errors);
        Log.Warning("L'affichage de l'iPhone s'est arrêté : {Errors}", errors);
        ShowStatus(IsUnplugged ? Strings.Get("IPhoneUnpluggedWait") : Strings.Format("IPhoneStopped", PyMobileDevice.Explain(errors)));

        if (!IsUnplugged)
        {
            Stopped?.Invoke(this, EventArgs.Empty);
        }
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

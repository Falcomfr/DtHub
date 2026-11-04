using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;

using DtHub.Core.Dofus;
using DtHub.Core.Localization;

using Serilog;

namespace DtHub.Mac.Platform;

/// <summary>
/// A navigation the main frame is about to make: <see cref="Cancel"/>
/// refuses it, as WebView2's NavigationStarting. A server redirect comes
/// here too, under the number of the navigation it continues.
/// </summary>
internal sealed class WebNavigationStarting(string uri, ulong navigationId, bool isRedirected) : EventArgs
{
    public string Uri { get; } = uri;

    public ulong NavigationId { get; } = navigationId;

    public bool IsRedirected { get; } = isRedirected;

    public bool Cancel { get; set; }
}

/// <summary>The end of a main frame navigation, as WebView2's NavigationCompleted.</summary>
internal sealed class WebNavigationCompleted(ulong navigationId, bool isSuccess, string webErrorStatus) : EventArgs
{
    public ulong NavigationId { get; } = navigationId;

    public bool IsSuccess { get; } = isSuccess;

    /// <summary>"Success", or WebKit's error domain, code and description.</summary>
    public string WebErrorStatus { get; } = webErrorStatus;

    /// <summary>
    /// Stopped rather than failed: by another navigation (NSURLError
    /// -999), by a refusal or a download (WebKit 102), or a plug-in (204).
    /// Nothing to show for it, as WebView2 shows no error page for these.
    /// </summary>
    public bool IsInterrupted =>
        !IsSuccess
        && (WebErrorStatus.StartsWith("NSURLErrorDomain -999 ", StringComparison.Ordinal)
            || WebErrorStatus.StartsWith("WebKitErrorDomain 102 ", StringComparison.Ordinal)
            || WebErrorStatus.StartsWith("WebKitErrorDomain 204 ", StringComparison.Ordinal));

    /// <summary>The error's description alone, for the reader.</summary>
    public string Description =>
        WebErrorStatus.Split(' ', 3) is [_, _, var text] ? text : WebErrorStatus;
}

/// <summary>
/// The Mac's web view, the PC's WebView2 (see Native/MacWebView.swift): a
/// WKWebView hosted in the window, with the share of CoreWebView2 the PC's
/// windows use, under the same names. Its events come on the interface
/// thread. One data store for the whole application, as the PC's single
/// profile: macOS keeps it in the application's own Library folders and
/// sizes its cache itself.
///
/// What WebView2 does by itself is done here for every window, as the
/// PC's WebViewEnvironment does: the ad hosts refused before the first
/// navigation, the history and the zoom (from the menu bar, see
/// MacMenus), the page's dialogs, file pickers and downloads.
///
/// The native view lives from construction to <see cref="Dispose"/>, not
/// with the control's place in the window: a page survives a relayout.
/// </summary>
internal sealed class MacWebView : NativeControlHost, IDisposable
{
    /// <summary>WebView2's zoom steps, Ctrl+plus and Ctrl+minus on the PC.</summary>
    private static readonly double[] ZoomSteps =
        [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];

    private static readonly WebEvent Callback = OnEvent;
    private static bool _languageSet;

    private readonly nint _host;
    private readonly GCHandle _self;
    private readonly Dictionary<long, TaskCompletionSource<string?>> _pending = [];
    private readonly TaskCompletionSource<bool> _rules = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _waitingAddress;
    private double _zoom = 1;
    private long _nextRequest;
    private bool _disposed;

    public MacWebView()
    {
        Focusable = true;

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // The pages hear DT Hub's language, that one alone, as the PC's
        // --accept-lang: set before the first view, for this process only.
        // Their dialogs' buttons speak it too.
        if (!_languageSet)
        {
            _languageSet = true;
            dthub_webview_set_language(Utf8(Strings.Spoken.TwoLetterISOLanguageName));
            dthub_webview_set_labels(Utf8(Strings.Get("MacOk")), Utf8(Strings.Get("Cancel")));
        }

        _self = GCHandle.Alloc(this);
        _host = dthub_webview_create(Callback, GCHandle.ToIntPtr(_self));

        BlockAdHosts();
    }

    /// <summary>True once the native view exists: always on a Mac.</summary>
    public bool IsReady => _host != 0 && !_disposed;

    public event EventHandler<WebNavigationStarting>? NavigationStarting;

    public event EventHandler<WebNavigationCompleted>? NavigationCompleted;

    /// <summary>The text a page posted through window.chrome.webview.postMessage.</summary>
    public event EventHandler<string>? WebMessageReceived;

    /// <summary>
    /// target="_blank" or window.open: never opened in a window of its
    /// own, the address goes to whoever listens.
    /// </summary>
    public event EventHandler<string>? NewWindowRequested;

    /// <summary>The address changed, the page's own history moves included.</summary>
    public event EventHandler? SourceChanged;

    /// <summary>
    /// A click in the page, which Avalonia never sees: for the window's
    /// popups to close as on a click anywhere else.
    /// </summary>
    public event EventHandler? NativePressed;

    /// <summary>
    /// The address of the last navigation let through, redirects followed:
    /// what to load again after a failure, which leaves <see cref="Source"/>
    /// on the page before.
    /// </summary>
    public string? LastNavigation { get; private set; }

    /// <summary>The address shown, empty before any page.</summary>
    public string Source
    {
        get
        {
            if (!IsReady)
            {
                return string.Empty;
            }

            var buffer = new byte[2048];
            var length = dthub_webview_source(_host, buffer, buffer.Length);

            // Longer than the buffer: asked again with the room it needs.
            if (length >= buffer.Length)
            {
                buffer = new byte[length + 1];
                length = dthub_webview_source(_host, buffer, buffer.Length);
            }

            return length < 0 || length >= buffer.Length ? string.Empty : Encoding.UTF8.GetString(buffer, 0, length);
        }
    }

    /// <summary>A script run before the page's own, on every document from now on, frames included.</summary>
    public void AddScriptToExecuteOnDocumentCreated(string script)
    {
        Ready();
        dthub_webview_add_script(_host, Utf8(script), mainFrameOnly: false);
    }

    /// <summary>Loads the address, once the ad hosts are refused: no request leaves before.</summary>
    public void Navigate(string url)
    {
        Ready();

        if (!System.Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException(url, nameof(url));
        }

        if (!_rules.Task.IsCompleted)
        {
            _waitingAddress = url;
            return;
        }

        if (!dthub_webview_navigate(_host, Utf8(url)))
        {
            throw new ArgumentException(url, nameof(url));
        }
    }

    /// <summary>The script's result as JSON, as WebView2's ExecuteScriptAsync.</summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        var (request, wait) = Request();
        dthub_webview_eval(_host, Utf8(script), request);
        return await wait.ConfigureAwait(true) ?? "null";
    }

    /// <summary>The value of a cookie the site of <paramref name="url"/> holds, or null.</summary>
    public Task<string?> GetCookieAsync(string name, string url)
    {
        var (request, wait) = Request();
        dthub_webview_cookie(_host, Utf8(name), Utf8(url), request);
        return wait;
    }

    public void DeleteCookies(string name, string url)
    {
        Ready();
        dthub_webview_delete_cookie(_host, Utf8(name), Utf8(url));
    }

    /// <summary>The previous page of the history, as Alt+Left on the PC.</summary>
    public void GoBack() => Go(-1);

    public void GoForward() => Go(1);

    public void Reload() => Go(0);

    /// <summary>One of WebView2's zoom steps up (positive) or down, as Ctrl+plus and Ctrl+minus.</summary>
    public void Zoom(int steps)
    {
        var index = Array.FindIndex(ZoomSteps, step => step >= _zoom - 0.001);
        index = index < 0 ? ZoomSteps.Length - 1 : index;

        // Between two steps, the first move up lands on the step just above.
        if (steps > 0 && ZoomSteps[index] > _zoom + 0.001)
        {
            steps--;
        }

        SetZoom(ZoomSteps[Math.Clamp(index + steps, 0, ZoomSteps.Length - 1)]);
    }

    /// <summary>The page's zoom, 1 being the site's own size.</summary>
    public void SetZoom(double zoom)
    {
        Ready();
        _zoom = zoom;
        dthub_webview_set_zoom(_host, zoom);
    }

    /// <summary>
    /// The keyboard to the page, even when Avalonia's focus never left
    /// this control: the window shown again, it is macOS's that matters.
    /// </summary>
    public void FocusPage()
    {
        Focus();

        if (IsReady)
        {
            dthub_webview_focus(_host);
        }
    }

    /// <summary>
    /// The keyboard to the page itself: Avalonia's focus on this control
    /// leaves macOS's on Avalonia's own view, where the page hears nothing.
    /// </summary>
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);

        if (IsReady)
        {
            dthub_webview_focus(_host);
        }
    }

    /// <summary>
    /// An Edit menu action (copy:, paste:...) for the page that has the
    /// keyboard; false when no page has it, for the caller to give it to
    /// Avalonia's focused field.
    /// </summary>
    public static bool TrySendEditAction(string selector) =>
        OperatingSystem.IsMacOS() && dthub_webview_edit_action(Utf8(selector));

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) =>
        IsReady
            ? new PlatformHandle(dthub_webview_view(_host), "NSView")
            : base.CreateNativeControlCore(parent);

    /// <summary>Not destroyed with its place: <see cref="Dispose"/> does.</summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!IsReady)
        {
            base.DestroyNativeControlCore(control);
        }
    }

    public void Dispose()
    {
        if (_disposed || _host == 0)
        {
            return;
        }

        _disposed = true;
        dthub_webview_destroy(_host);
        _self.Free();

        foreach (var wait in _pending.Values)
        {
            wait.TrySetCanceled();
        }

        _pending.Clear();
        _rules.TrySetResult(false);
    }

    /// <summary>
    /// Requests to the ad hosts, and their subdomains, never leave, as the
    /// PC's --host-resolver-rules, in every window. Navigations wait for it.
    /// </summary>
    private void BlockAdHosts()
    {
        var list = DofusTouchMap.BlockedAdHosts;
        var rules = "[" + string.Join(',', list.Select(host =>
            "{\"trigger\":{\"url-filter\":\"^[a-z]+://([^/:]+\\\\.)?"
            + host.Replace(".", "\\\\.", StringComparison.Ordinal)
            + "[:/]\"},\"action\":{\"type\":\"block\"}}")) + "]";

        dthub_webview_block(_host, Utf8("dthub-blocked-" + string.Join('-', list)), Utf8(rules));
    }

    private void Go(int step)
    {
        if (IsReady)
        {
            dthub_webview_go(_host, step);
        }
    }

    private void Ready()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_host == 0)
        {
            throw new PlatformNotSupportedException();
        }
    }

    private (long Request, Task<string?> Wait) Request()
    {
        Ready();

        var request = ++_nextRequest;
        var wait = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request] = wait;
        return (request, wait.Task);
    }

    private int Handle(int kind, string? text, long number)
    {
        switch (kind)
        {
            case 1 or 10:
                var starting = new WebNavigationStarting(text ?? string.Empty, (ulong)number, isRedirected: kind == 10);
                NavigationStarting?.Invoke(this, starting);

                if (!starting.Cancel)
                {
                    LastNavigation = starting.Uri;
                }

                return starting.Cancel ? 1 : 0;

            case 2:
                var success = text == "1";
                NavigationCompleted?.Invoke(this, new WebNavigationCompleted(
                    (ulong)number, success, success ? "Success" : (text ?? string.Empty).TrimStart('0', ' ')));
                break;

            case 3 when text is not null:
                WebMessageReceived?.Invoke(this, text);
                break;

            case 4 when text is not null:
                NewWindowRequested?.Invoke(this, text);
                break;

            case 5:
                SourceChanged?.Invoke(this, EventArgs.Empty);
                break;

            case 6 or 8:
                if (_pending.Remove(number, out var done))
                {
                    done.TrySetResult(text);
                }

                break;

            case 7:
                if (_pending.Remove(number, out var failed))
                {
                    failed.TrySetException(new InvalidOperationException(text));
                }

                break;

            case 9:
                OnRulesReady(number == 1);
                break;

            case 11 when number == 1:
                Log.Information("Téléchargement enregistré : {Fichier}", text);
                break;

            case 11:
                Log.Warning("Le téléchargement a échoué : {Erreur}", text);
                break;

            case 12:
                NativePressed?.Invoke(this, EventArgs.Empty);
                break;

            default:
                break;
        }

        return 0;
    }

    /// <summary>The rules in place, or refused by WebKit: the page waited for loads either way.</summary>
    private void OnRulesReady(bool applied)
    {
        if (!applied)
        {
            Log.Warning("Les hôtes publicitaires n'ont pas pu être bloqués dans le moteur de rendu.");
        }

        _rules.TrySetResult(applied);

        if (_waitingAddress is { } address)
        {
            _waitingAddress = null;
            _ = dthub_webview_navigate(_host, Utf8(address));
        }
    }

    /// <summary>From the native side, on the main thread; nothing may escape into it.</summary>
    private static int OnEvent(nint context, int kind, nint text, long number)
    {
        try
        {
            return GCHandle.FromIntPtr(context).Target is MacWebView view && !view._disposed
                ? view.Handle(kind, text == 0 ? null : Marshal.PtrToStringUTF8(text), number)
                : 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Le moteur de rendu a renvoyé une erreur sur l'événement {Kind}.", kind.ToString(CultureInfo.InvariantCulture));
            return 0;
        }
    }

    /// <summary>A string as the native side reads it: UTF-8, ended by a zero.</summary>
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WebEvent(nint context, int kind, nint text, long number);

    private const string Library = "libDtHubMac";

    [DllImport(Library)]
    private static extern nint dthub_webview_create(WebEvent callback, nint context);

    [DllImport(Library)]
    private static extern nint dthub_webview_view(nint host);

    [DllImport(Library)]
    private static extern void dthub_webview_destroy(nint host);

    [DllImport(Library)]
    private static extern void dthub_webview_add_script(
        nint host,
        byte[] source,
        [MarshalAs(UnmanagedType.I1)] bool mainFrameOnly);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool dthub_webview_navigate(nint host, byte[] address);

    [DllImport(Library)]
    private static extern int dthub_webview_source(nint host, byte[] buffer, int capacity);

    [DllImport(Library)]
    private static extern void dthub_webview_eval(nint host, byte[] script, long request);

    [DllImport(Library)]
    private static extern void dthub_webview_cookie(
        nint host,
        byte[] name,
        byte[] address,
        long request);

    [DllImport(Library)]
    private static extern void dthub_webview_delete_cookie(
        nint host,
        byte[] name,
        byte[] address);

    [DllImport(Library)]
    private static extern void dthub_webview_block(
        nint host,
        byte[] identifier,
        byte[] rules);

    [DllImport(Library)]
    private static extern void dthub_webview_set_zoom(nint host, double zoom);

    [DllImport(Library)]
    private static extern void dthub_webview_go(nint host, int step);

    [DllImport(Library)]
    private static extern void dthub_webview_focus(nint host);

    [DllImport(Library)]
    private static extern void dthub_webview_set_labels(byte[] ok, byte[] cancel);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool dthub_webview_edit_action(byte[] selector);

    [DllImport(Library)]
    private static extern void dthub_webview_set_language(byte[] language);
}

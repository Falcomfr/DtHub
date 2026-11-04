using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia.Controls;
using Avalonia.Platform;

using DtHub.Core.Localization;

using Serilog;

namespace DtHub.Mac.Platform;

/// <summary>
/// A navigation the main frame is about to make: <see cref="Cancel"/>
/// refuses it, as WebView2's NavigationStarting.
/// </summary>
internal sealed class WebNavigationStarting(string uri, ulong navigationId) : EventArgs
{
    public string Uri { get; } = uri;

    public ulong NavigationId { get; } = navigationId;

    public bool Cancel { get; set; }
}

/// <summary>The end of a main frame navigation, as WebView2's NavigationCompleted.</summary>
internal sealed class WebNavigationCompleted(ulong navigationId, bool isSuccess, string webErrorStatus) : EventArgs
{
    public ulong NavigationId { get; } = navigationId;

    public bool IsSuccess { get; } = isSuccess;

    /// <summary>"Success", or WebKit's error domain, code and description.</summary>
    public string WebErrorStatus { get; } = webErrorStatus;
}

/// <summary>
/// The Mac's web view, the PC's WebView2 (see Native/MacWebView.swift): a
/// WKWebView hosted in the window, with the share of CoreWebView2 the PC's
/// windows use, under the same names. Its events come on the interface
/// thread. One data store for the whole application, as the PC's single
/// profile: macOS keeps it in the application's own Library folders and
/// sizes its cache itself.
///
/// The native view lives from construction to <see cref="Dispose"/>, not
/// with the control's place in the window: a page survives a relayout.
/// </summary>
internal sealed class MacWebView : NativeControlHost, IDisposable
{
    private static readonly WebEvent Callback = OnEvent;
    private static bool _languageSet;

    private readonly nint _host;
    private readonly GCHandle _self;
    private readonly Dictionary<long, TaskCompletionSource<string?>> _pending = [];
    private TaskCompletionSource<bool>? _rules;
    private long _nextRequest;
    private bool _disposed;

    public MacWebView()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // The pages hear DT Hub's language, that one alone, as the PC's
        // --accept-lang: set before the first view, for this process only.
        if (!_languageSet)
        {
            _languageSet = true;
            dthub_webview_set_language(Utf8(Strings.Spoken.TwoLetterISOLanguageName));
        }

        _self = GCHandle.Alloc(this);
        _host = dthub_webview_create(Callback, GCHandle.ToIntPtr(_self));
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

    /// <summary>The address shown, empty before any page.</summary>
    public string Source
    {
        get
        {
            if (!IsReady)
            {
                return string.Empty;
            }

            var buffer = new byte[8192];

            if (!dthub_webview_source(_host, buffer, buffer.Length))
            {
                return string.Empty;
            }

            var end = Array.IndexOf(buffer, (byte)0);
            return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        }
    }

    /// <summary>A script run before the page's own, on every document from now on, frames included.</summary>
    public void AddScriptToExecuteOnDocumentCreated(string script)
    {
        Ready();
        dthub_webview_add_script(_host, Utf8(script), mainFrameOnly: false);
    }

    public void Navigate(string url)
    {
        Ready();

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

    /// <summary>
    /// Requests to these hosts, and their subdomains, never leave, as the
    /// PC's --host-resolver-rules. Awaited before the first navigation.
    /// </summary>
    public Task BlockHostsAsync(IEnumerable<string> hosts)
    {
        Ready();

        var list = hosts.ToList();
        var rules = "[" + string.Join(',', list.Select(host =>
            "{\"trigger\":{\"url-filter\":\"^[a-z]+://([^/:]+\\\\.)?"
            + host.Replace(".", "\\\\.", StringComparison.Ordinal)
            + "[:/]\"},\"action\":{\"type\":\"block\"}}")) + "]";

        _rules = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        dthub_webview_block(_host, Utf8("dthub-blocked-" + string.Join('-', list)), Utf8(rules));
        return _rules.Task;
    }

    /// <summary>The page's zoom, 1 being the site's own size.</summary>
    public void SetZoom(double zoom)
    {
        Ready();
        dthub_webview_set_zoom(_host, zoom);
    }

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
        _rules?.TrySetResult(false);
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
            case 1:
                var starting = new WebNavigationStarting(text ?? string.Empty, (ulong)number);
                NavigationStarting?.Invoke(this, starting);
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
                _rules?.TrySetResult(number == 1);
                break;

            default:
                break;
        }

        return 0;
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
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool dthub_webview_source(nint host, byte[] buffer, int capacity);

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
    private static extern void dthub_webview_set_language(byte[] language);
}

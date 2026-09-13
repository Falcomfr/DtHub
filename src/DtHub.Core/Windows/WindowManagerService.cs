using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;

namespace DtHub.Core.Windows;

/// <summary>
/// Places the game windows. All of them receive exactly the same
/// rectangle and therefore overlap perfectly: switching from one to
/// another with the keyboard moves nothing on screen.
/// </summary>
public sealed class WindowManagerService
{
    private readonly IWindowController _controller;

    /// <summary>
    /// Raw access to the windows, for whatever is not a placement.
    ///
    /// The tabbed frame docks and undocks itself: routing every one
    /// of its gestures through this service, which only speaks of
    /// sessions and screens, would have mixed two separate concerns.
    /// </summary>
    public IWindowController Controller => _controller;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private int _focusIndex = -1;

    /// <summary>
    /// Last size seen per session, so a correction is made only once
    /// the gesture is finished.
    /// </summary>
    private readonly Dictionary<string, ScreenRect> _lastSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// Last game window to have been in the foreground.
    ///
    /// It cannot be read at the moment of rearranging: clicking the
    /// button brings the configurator to the foreground, and no game
    /// window is there anymore. It must therefore have been tracked
    /// beforehand.
    /// </summary>
    private string? _lastActive;

    /// <summary>
    /// Geometry of each window just before switching to full screen.
    ///
    /// Leaving it must give each one back its place. Without this
    /// memory, coming back started from the full screen rectangle and
    /// stacked them all in the same spot.
    /// </summary>
    private readonly Dictionary<string, ScreenRect> _beforeFullscreen = new(StringComparer.Ordinal);

    /// <summary>Size in force before switching to full screen.</summary>
    private int _percentBeforeFullscreen = 100;


    public WindowManagerService(
        IWindowController controller,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _controller = controller;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>Position of the window block on the screen.</summary>
    public WindowAnchor Anchor { get; set; } = WindowAnchor.MiddleLeft;

    /// <summary>Configured sizes, proportional to the screen.</summary>
    public WindowSizePresets Presets { get; set; } = WindowSizePresets.Default;

    /// <summary>Current size, by its index in the configured sizes.</summary>
    public int SizeIndex { get; private set; } = 1;

    /// <summary>
    /// Size freely chosen with the slider, as a percentage of the
    /// usable area. It takes precedence over the index as long as no
    /// size shortcut has been used: the slider is a continuous value,
    /// the shortcuts four marks on that same scale.
    /// </summary>
    public int? CustomSizePercent { get; private set; }

    /// <summary>Current size, as a percentage of the usable area.</summary>
    public int SizePercent => CustomSizePercent ?? Presets.PercentageAt(SizeIndex);

    /// <summary>
    /// True if the current size is full screen with no border.
    /// </summary>
    public bool IsFullscreen => CustomSizePercent is null && Presets.IsFullscreen(SizeIndex);


    /// <summary>
    /// Maximum wait for the scrcpy window. It only appears once the
    /// first frame is received, which takes a moment.
    /// </summary>
    public TimeSpan WindowAppearanceTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan WindowPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Available screens, for the settings.</summary>
    public IReadOnlyList<MonitorInfo> GetMonitors() => _controller.GetMonitors();

    /// <summary>
    /// Rectangle the game windows will occupy, without moving
    /// anything. Used to place the configurator elsewhere.
    /// </summary>
    public ScreenRect? PreviewGameArea(double sourceAspectRatio)
    {
        var monitors = _controller.GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, preferredDeviceName: null);

        return Compute(monitor, sourceAspectRatio, (0, 0));
    }

    /// <summary>
    /// Rectangle of a window on a given screen. Full screen covers
    /// the entire screen, taskbar included.
    /// </summary>
    /// <param name="chrome">
    /// Footprint of the title bar and the borders, measured on the
    /// window. The aspect ratio applies to the client area, the one
    /// scrcpy fills: ignoring it leaves black bars on the sides.
    /// </param>
    private ScreenRect Compute(MonitorInfo monitor, double sourceAspectRatio, (int Width, int Height) chrome)
    {
        if (IsFullscreen)
        {
            return monitor.Bounds;
        }

        var work = UsableArea(monitor);
        var (width, height) = ComputeSize(monitor, sourceAspectRatio, chrome);

        return WindowLayoutCalculator.Place(work, width, height, Anchor);
    }

    /// <summary>
    /// Size of a window, without deciding its place. The aspect ratio
    /// applies to the client area, the one scrcpy fills.
    /// </summary>
    private (int Width, int Height) ComputeSize(
        MonitorInfo monitor,
        double sourceAspectRatio,
        (int Width, int Height) chrome)
    {
        var work = UsableArea(monitor);
        var fraction = Math.Clamp(SizePercent, 20, 100) / 100.0;

        var availableWidth = Math.Max(1, (int)Math.Round(work.Width * fraction));
        var availableHeight = Math.Max(1, (int)Math.Round(work.Height * fraction));

        if (sourceAspectRatio <= 0)
        {
            return (availableWidth, availableHeight);
        }

        var (clientWidth, clientHeight) = WindowLayoutCalculator.FitToAspect(
            Math.Max(1, availableWidth - chrome.Width),
            Math.Max(1, availableHeight - chrome.Height),
            sourceAspectRatio);

        return (clientWidth + chrome.Width, clientHeight + chrome.Height);
    }

    private static ScreenRect UsableArea(MonitorInfo monitor) =>
        monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;

    /// <summary>
    /// Changes the size of the windows without moving them, by
    /// multiplying each one's size by the same factor.
    ///
    /// Giving them all the same size would erase deliberate
    /// differences: a window made smaller than another on purpose
    /// must stay that way. A size shortcut or the slider only ask to
    /// grow or shrink, not to make uniform nor to rearrange. Only
    /// full screen covers the entire screen.
    /// </summary>
    public async Task<int> ScaleInPlaceAsync(
        IReadOnlyList<ScrcpySession> sessions,
        double factor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (IsFullscreen)
        {
            return await ArrangeAsync(sessions, cancellationToken).ConfigureAwait(false);
        }

        var monitors = _controller.GetMonitors();

        if (monitors.Count == 0)
        {
            return 0;
        }

        var resized = 0;

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

            if (handle == 0)
            {
                continue;
            }

            _controller.SetBorderless(handle, borderless: false);

            if (_controller.GetWindowRect(handle) is not { } current || current.IsEmpty)
            {
                continue;
            }

            var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, current.CenterX, current.CenterY);
            var target = Rescale(current, factor, UsableArea(monitor));

            if (target == current)
            {
                continue;
            }

            _controller.MoveWindow(handle, target);
            _lastSeen[session.Id] = target;
            resized++;
        }

        return resized;
    }

    /// <summary>
    /// Resizes a rectangle while keeping its relative position within
    /// the usable area.
    ///
    /// The share of free space to its left stays the same: stuck to
    /// the left it stays stuck to the left, centered it stays
    /// centered, in a corner it grows from that corner. Keeping the
    /// top left corner and then pulling the window back inside the
    /// screen used to push it as soon as it grew near an edge.
    /// </summary>
    private static ScreenRect Rescale(ScreenRect current, double factor, ScreenRect work)
    {
        var width = Math.Clamp((int)Math.Round(current.Width * factor), 120, Math.Max(120, work.Width));
        var height = Math.Clamp((int)Math.Round(current.Height * factor), 80, Math.Max(80, work.Height));

        return new ScreenRect(
            Slide(current.X, current.Width, width, work.X, work.Width),
            Slide(current.Y, current.Height, height, work.Y, work.Height),
            width,
            height);
    }

    /// <summary>
    /// Brings a window back inside the usable area without resizing
    /// it.
    ///
    /// Only the overflow is corrected, and by the smallest possible
    /// move: a window that is entirely visible is never touched.
    /// </summary>
    private static ScreenRect KeepInside(ScreenRect rect, ScreenRect work)
    {
        if (rect.Width > work.Width || rect.Height > work.Height)
        {
            return rect;
        }

        return rect with
        {
            X = Math.Clamp(rect.X, work.X, work.X + work.Width - rect.Width),
            Y = Math.Clamp(rect.Y, work.Y, work.Y + work.Height - rect.Height),
        };
    }

    /// <summary>
    /// New X or Y coordinate, keeping the share of free space
    /// constant.
    /// </summary>
    private static int Slide(int position, int before, int after, int origin, int span)
    {
        var freeBefore = span - before;
        var freeAfter = span - after;

        var share = freeBefore > 0 ? Math.Clamp((position - origin) / (double)freeBefore, 0, 1) : 0.5;

        return origin + (int)Math.Round(share * freeAfter);
    }

    /// <summary>
    /// Brings the windows back to the shape of their display, under
    /// the locked aspect ratio.
    ///
    /// This mode scales the image: it only fills the window at that
    /// shape, and departing from it leaves a band. When the width is
    /// free, nothing is corrected: resizing is free in both
    /// directions there.
    /// </summary>
    /// <returns>Number of windows corrected.</returns>
    public int EnforceAspect(IReadOnlyList<ScrcpySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (IsFullscreen)
        {
            return 0;
        }

        var corrected = 0;
        var monitors = _controller.GetMonitors();

        if (monitors.Count == 0)
        {
            return 0;
        }

        foreach (var session in sessions.Where(s => s.IsAlive && s.WindowHandle != 0))
        {
            if (session.SourceAspectRatio <= 0
                || _controller.GetWindowRect(session.WindowHandle) is not { } outer
                || outer.IsEmpty)
            {
                continue;
            }

            var settled = _lastSeen.TryGetValue(session.Id, out var previous) && previous == outer;
            _lastSeen[session.Id] = outer;

            if (!settled)
            {
                continue;
            }

            // A resize with the mouse is handled by Windows, which
            // keeps the opposite edge, and by scrcpy, which locks the
            // aspect ratio by growing downward. A window sitting at
            // the bottom of the screen therefore goes off it as soon
            // as it is widened. It is brought back inside, without
            // changing its size: this is the only case where we
            // touch what the user has just done with their own
            // hands.
            var work = UsableArea(
                WindowLayoutCalculator.ChooseMonitor(monitors, outer.CenterX, outer.CenterY));

            if (KeepInside(outer, work) is var inside && inside != outer)
            {
                _controller.MoveWindow(session.WindowHandle, inside);
                _lastSeen[session.Id] = inside;
                outer = inside;
                corrected++;
            }

            var chrome = MeasureChrome(session.WindowHandle);

            // Locked aspect ratio: the image is scaled, it only
            // fills the window at the shape of the display.
            // Departing from it leaves a band, above as well as
            // below.
            var wanted = (int)Math.Round(
                (outer.Width - chrome.Width) / session.SourceAspectRatio) + chrome.Height;

            if (Math.Abs(outer.Height - wanted) <= 2)
            {
                continue;
            }

            // The corrected height pushes toward the available
            // space rather than always downward.
            var target = KeepInside(
                outer with
                {
                    Y = Slide(outer.Y, outer.Height, wanted, work.Y, work.Height),
                    Height = wanted,
                },
                work);

            _controller.MoveWindow(session.WindowHandle, target);
            _lastSeen[session.Id] = target;
            corrected++;
        }

        return corrected;
    }

    /// <summary>
    /// Difference between the outer rectangle of a window and its
    /// client area. Zero if the measurement fails, in which case the
    /// calculation falls back to the previous behavior.
    /// </summary>
    private (int Width, int Height) MeasureChrome(nint handle)
    {
        if (_controller.GetWindowRect(handle) is not { } outer
            || _controller.GetClientRect(handle) is not { } client
            || client.Width <= 0 || client.Height <= 0)
        {
            return (0, 0);
        }

        return (Math.Max(0, outer.Width - client.Width), Math.Max(0, outer.Height - client.Height));
    }

    /// <summary>
    /// Applies a size to all the windows and rearranges them. An
    /// out-of-range index is clamped back within bounds rather than
    /// refused.
    /// </summary>
    public Task<int> ApplySizeAsync(
        IReadOnlyList<ScrcpySession> sessions,
        int sizeIndex,
        CancellationToken cancellationToken = default)
    {
        var wasFullscreen = IsFullscreen;
        var previous = SizePercent;

        SizeIndex = Math.Clamp(sizeIndex, 0, Math.Max(0, Presets.Count - 1));

        // A size shortcut takes control back from the slider.
        CustomSizePercent = null;

        return ResizeAsync(sessions, wasFullscreen, previous, cancellationToken);
    }

    /// <summary>
    /// Applies the new size, handling entering and leaving full
    /// screen separately.
    /// </summary>
    private Task<int> ResizeAsync(
        IReadOnlyList<ScrcpySession> sessions,
        bool wasFullscreen,
        int previousPercent,
        CancellationToken cancellationToken) => (wasFullscreen, IsFullscreen) switch
        {
            (false, true) => EnterFullscreenAsync(sessions, previousPercent, cancellationToken),
            (true, false) => LeaveFullscreenAsync(sessions, cancellationToken),
            (true, true) => Task.FromResult(0),
            _ => ResizeToPercentAsync(sessions, cancellationToken),
        };

    /// <summary>
    /// Gives each window the size the percentage designates, without
    /// moving it.
    ///
    /// This used to be a factor, not a size: the new share was
    /// divided by the old one, and the current rectangle multiplied
    /// by that ratio. Asking again for the share already in force
    /// therefore gave a factor of one, and the shortcut did nothing;
    /// coming from the high share toward the low one shrank what was
    /// there rather than setting the minimum. The maximum did not
    /// open the window all the way and the minimum did not close it
    /// to its smallest, which the README nonetheless promised in
    /// plain words.
    ///
    /// What the factor protected is lost, and it must be said: two
    /// windows deliberately of different sizes now receive the same
    /// one. Their places, though, are kept: the share of free space
    /// to the left and above stays the same, so that a side by side
    /// arrangement stays left and right, only resized.
    /// </summary>
    private async Task<int> ResizeToPercentAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken)
    {
        var monitors = _controller.GetMonitors();

        if (monitors.Count == 0)
        {
            return 0;
        }

        var resized = 0;

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

            if (handle == 0)
            {
                continue;
            }

            _controller.SetBorderless(handle, borderless: false);

            if (_controller.GetWindowRect(handle) is not { } current || current.IsEmpty)
            {
                continue;
            }

            if (ResizedRect(handle, session.SourceAspectRatio, MeasureChrome(handle))
                is not { } target
                || target == current)
            {
                continue;
            }

            _controller.MoveWindow(handle, target);
            _lastSeen[session.Id] = target;
            resized++;
        }

        return resized;
    }

    /// <summary>
    /// Switches to full screen, each window covering the screen it
    /// sits on, and remembers where it came from.
    /// </summary>
    private async Task<int> EnterFullscreenAsync(
        IReadOnlyList<ScrcpySession> sessions,
        int previousPercent,
        CancellationToken cancellationToken)
    {
        var monitors = _controller.GetMonitors();

        if (sessions.Count == 0 || monitors.Count == 0)
        {
            return 0;
        }

        _beforeFullscreen.Clear();
        _percentBeforeFullscreen = previousPercent;

        var moved = 0;

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

            if (handle == 0 || _controller.GetWindowRect(handle) is not { } rect || rect.IsEmpty)
            {
                continue;
            }

            _beforeFullscreen[session.Target.Key] = rect;

            var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, rect.CenterX, rect.CenterY);

            _controller.SetBorderless(handle, borderless: true);
            _controller.MoveWindow(handle, monitor.Bounds);
            _lastSeen[session.Id] = monitor.Bounds;
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// Leaves full screen and gives each window back the place it
    /// had, scaled if the requested size is not the one from before.
    /// </summary>
    private async Task<int> LeaveFullscreenAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken)
    {
        var monitors = _controller.GetMonitors();

        if (sessions.Count == 0 || monitors.Count == 0)
        {
            return 0;
        }

        var factor = _percentBeforeFullscreen > 0
            ? (double)SizePercent / _percentBeforeFullscreen
            : 1;

        var moved = 0;

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

            if (handle == 0)
            {
                continue;
            }

            _controller.SetBorderless(handle, borderless: false);

            var chrome = MeasureChrome(handle);

            var rect = _beforeFullscreen.TryGetValue(session.Target.Key, out var before)
                ? Rescale(
                      before,
                      factor,
                      UsableArea(WindowLayoutCalculator.ChooseMonitor(monitors, before.CenterX, before.CenterY)))
                : Compute(
                      WindowLayoutCalculator.ChooseMonitor(monitors, preferredDeviceName: null),
                      session.SourceAspectRatio,
                      chrome);

            _controller.MoveWindow(handle, rect);
            _lastSeen[session.Id] = rect;
            moved++;
        }

        _beforeFullscreen.Clear();

        return moved;
    }

    /// <summary>
    /// Footprint of a window's frame on the chosen screen. Known
    /// before any window exists, to ask scrcpy for a display of
    /// exactly the client area's size.
    /// </summary>
    public WindowFrame WindowChrome() =>
        _controller.GetWindowChrome(monitorDeviceName: null);

    /// <summary>
    /// Full bounds of the screen where a window is about to open,
    /// taskbar included.
    ///
    /// This is the screen of its remembered geometry, and the main
    /// screen otherwise. Taking a single reference screen would give
    /// the wrong aspect ratio to the display of a window left on a
    /// second screen of a different shape, and it would be born
    /// malformed.
    /// </summary>
    public ScreenRect? MonitorBoundsFor(StoredWindowRect? remembered)
    {
        var monitors = _controller.GetMonitors();

        if (monitors.Count == 0)
        {
            return null;
        }

        var restored = remembered is null
            ? null
            : WindowLayoutCalculator.RestoreRemembered(
                  remembered.Bounds, remembered.MonitorDeviceName, remembered.Monitor, monitors);

        return restored is { } rect
            ? WindowLayoutCalculator.ChooseMonitor(monitors, rect.CenterX, rect.CenterY).Bounds
            : WindowLayoutCalculator.ChooseMonitor(monitors, preferredDeviceName: null).Bounds;
    }

    /// <summary>Usable area of the chosen screen.</summary>
    public ScreenRect? WorkArea()
    {
        var monitors = _controller.GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, preferredDeviceName: null);

        return monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
    }

    /// <summary>
    /// Finds a session's window by its process. Each scrcpy process
    /// opens only one visible window, which is enough to identify it
    /// and leaves the title entirely to the name chosen by the user.
    /// The title only serves as a tiebreaker if several windows were
    /// to appear.
    /// </summary>
    public async Task<nint> ResolveWindowAsync(
        ScrcpySession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.WindowHandle != 0 && _controller.IsWindow(session.WindowHandle))
        {
            return session.WindowHandle;
        }

        var deadline = DateTimeOffset.UtcNow + WindowAppearanceTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var windows = _controller.FindWindows(session.ProcessId);

            var match = windows.Count switch
            {
                0 => default,
                1 => windows[0],
                _ => windows.FirstOrDefault(
                         w => string.Equals(w.Title, session.WindowTitle, StringComparison.Ordinal)) is
                { Handle: not 0 } titled
                    ? titled
                    : windows[0],
            };

            if (match.Handle != 0)
            {
                session.WindowHandle = match.Handle;
                return match.Handle;
            }

            if (DateTimeOffset.UtcNow >= deadline || !session.IsAlive)
            {
                return 0;
            }

            await _delay(WindowPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Places all the windows at the same spot, at the configured
    /// position and size. This is the quick rearrangement: it
    /// deliberately overwrites the geometry the user had given each
    /// window.
    /// </summary>
    /// <returns>Number of windows actually moved.</returns>
    public async Task<int> ArrangeAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default)
    {
        var applied = await ApplyLayoutAsync(sessions, remembered: null, cancellationToken)
            .ConfigureAwait(false);

        return applied.Count;
    }

    /// <summary>
    /// Places each window where it had been left, and falls back to
    /// the calculated placement for those that do not yet have a
    /// geometry.
    /// </summary>
    /// <returns>The rectangles actually applied, by instance key.</returns>
    public Task<IReadOnlyList<(string Key, ScreenRect Rect)>> RestoreAsync(
        IReadOnlyList<ScrcpySession> sessions,
        IReadOnlyDictionary<string, StoredWindowRect> remembered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remembered);

        return ApplyLayoutAsync(sessions, remembered, cancellationToken);
    }

    /// <summary>
    /// Current geometry of each live window, by instance key.
    ///
    /// Full screen is set aside: its rectangle equals the entire
    /// screen and the window is borderless there. Remembering it and
    /// then restoring it at normal size would give a bordered window
    /// overflowing under the taskbar. A minimized window is set
    /// aside too, its rectangle meaning nothing.
    /// </summary>
    public IReadOnlyList<(string Key, StoredWindowRect Rect)> CaptureGeometries(
        IReadOnlyList<ScrcpySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (IsFullscreen)
        {
            return [];
        }

        var monitors = _controller.GetMonitors();

        if (monitors.Count == 0)
        {
            return [];
        }

        List<(string, StoredWindowRect)> captured = [];

        foreach (var session in sessions.Where(s => s.IsAlive && s.WindowHandle != 0))
        {
            if (_controller.GetWindowRect(session.WindowHandle) is not { } rect || rect.IsEmpty)
            {
                continue;
            }

            var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, rect.CenterX, rect.CenterY);

            captured.Add((session.Target.Key, StoredWindowRect.From(rect, monitor)));
        }

        return captured;
    }

    /// <summary>
    /// Applies a layout. Without remembered geometries, all the
    /// windows receive the rectangle calculated from the anchor.
    /// </summary>
    private async Task<IReadOnlyList<(string Key, ScreenRect Rect)>> ApplyLayoutAsync(
        IReadOnlyList<ScrcpySession> sessions,
        IReadOnlyDictionary<string, StoredWindowRect>? remembered,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var monitors = _controller.GetMonitors();
        if (monitors.Count == 0)
        {
            return [];
        }

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, preferredDeviceName: null);
        List<(string, ScreenRect)> applied = [];

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);
            if (handle == 0)
            {
                continue;
            }

            // The border only disappears in full screen, and comes
            // back on leaving.
            _controller.SetBorderless(handle, IsFullscreen);

            (int Width, int Height) chrome = IsFullscreen ? (0, 0) : MeasureChrome(handle);
            var rect = Resolve(session, monitor, monitors, chrome, remembered);

            // A window already in place is not moved. scrcpy opens
            // it at the rectangle we asked for: moving it anyway has
            // nothing to correct, and the jump used to show on every
            // opening.
            if (_controller.GetWindowRect(handle) != rect)
            {
                _controller.MoveWindow(handle, rect);
            }

            applied.Add((session.Target.Key, rect));
        }

        return applied;
    }

    /// <summary>
    /// Rectangle of a window: the one it had when it is still valid,
    /// the anchor-based calculation otherwise. Full screen always
    /// takes precedence over a remembered geometry.
    /// </summary>
    private ScreenRect Resolve(
        ScrcpySession session,
        MonitorInfo monitor,
        IReadOnlyList<MonitorInfo> monitors,
        (int Width, int Height) chrome,
        IReadOnlyDictionary<string, StoredWindowRect>? remembered)
    {
        if (!IsFullscreen
            && remembered is not null
            && remembered.TryGetValue(session.Target.Key, out var stored)
            && WindowLayoutCalculator.RestoreRemembered(
                stored.Bounds, stored.MonitorDeviceName, stored.Monitor, monitors) is { } restored)
        {
            return restored;
        }

        return Compute(monitor, session.SourceAspectRatio, chrome);
    }

    /// <summary>
    /// The rectangle a window should occupy at the current size,
    /// without changing place: its share of free space to the left
    /// and above is kept, exactly as for the game windows.
    ///
    /// These two methods are published for the tabbed frame. It is
    /// not a session, so it does not appear in any of the lists the
    /// other methods receive, and yet it is arranged like a game
    /// window. Giving them the calculation rather than a second
    /// implementation is the only way the two do not drift apart.
    /// </summary>
    /// <param name="handle">
    /// Window targeted, to know which screen carries it.
    /// </param>
    /// <param name="aspectRatio">
    /// Aspect ratio of the image, zero if unknown.
    /// </param>
    /// <param name="chrome">Footprint of the window chrome.</param>
    public ScreenRect? ResizedRect(nint handle, double aspectRatio, (int Width, int Height) chrome)
    {
        if (MonitorOf(handle) is not { } monitor)
        {
            return null;
        }

        var work = UsableArea(monitor);
        var (width, height) = ComputeSize(monitor, aspectRatio, chrome);

        if (_controller.GetWindowRect(handle) is not { } current || current.IsEmpty)
        {
            return WindowLayoutCalculator.Place(work, width, height, Anchor);
        }

        return KeepInside(
            new ScreenRect(
                Slide(current.X, current.Width, width, work.X, work.Width),
                Slide(current.Y, current.Height, height, work.Y, work.Height),
                width,
                height),
            work);
    }

    /// <summary>
    /// The rectangle a window should occupy at the current size and
    /// position from the settings, that of the grid of nine anchors.
    /// </summary>
    public ScreenRect? AnchoredRect(nint handle, double aspectRatio, (int Width, int Height) chrome) =>
        MonitorOf(handle) is { } monitor ? Compute(monitor, aspectRatio, chrome) : null;

    /// <summary>
    /// The full bounds of the screen that carries a window, taskbar
    /// included. This is what full screen covers.
    /// </summary>
    public ScreenRect? ScreenBoundsFor(nint handle) => MonitorOf(handle)?.Bounds;

    /// <summary>The usable area of the screen that carries a window.</summary>
    public ScreenRect? WorkAreaFor(nint handle) =>
        MonitorOf(handle) is { } monitor ? UsableArea(monitor) : null;

    /// <summary>
    /// The screen that carries a window, or the main screen
    /// otherwise.
    /// </summary>
    private MonitorInfo? MonitorOf(nint handle)
    {
        var monitors = _controller.GetMonitors();

        if (monitors.Count == 0)
        {
            return null;
        }

        return handle != 0 && _controller.GetWindowRect(handle) is { } current && !current.IsEmpty
            ? WindowLayoutCalculator.ChooseMonitor(monitors, current.CenterX, current.CenterY)
            : WindowLayoutCalculator.ChooseMonitor(monitors, preferredDeviceName: null);
    }

    /// <summary>
    /// Applies the free size from the slider, without moving the
    /// windows.
    /// </summary>
    public Task<int> ApplyPercentAsync(
        IReadOnlyList<ScrcpySession> sessions,
        int percent,
        CancellationToken cancellationToken = default)
    {
        var wasFullscreen = IsFullscreen;
        var previous = SizePercent;

        CustomSizePercent = Math.Clamp(percent, 20, 100);

        return ResizeAsync(sessions, wasFullscreen, previous, cancellationToken);
    }

    /// <summary>
    /// Rewrites the title of every open window. scrcpy only sets its
    /// own at startup: without this, the shortcut reminder would
    /// stay stale until the next opening.
    /// </summary>
    /// <returns>Number of windows renamed.</returns>
    public int Retitle(IReadOnlyList<ScrcpySession> sessions, Func<ScrcpySession, string> title)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(title);

        var renamed = 0;

        foreach (var session in sessions.Where(s => s.IsAlive && s.WindowHandle != 0))
        {
            _controller.SetTitle(session.WindowHandle, title(session));
            renamed++;
        }

        return renamed;
    }


    /// <summary>
    /// Stacks the windows onto one of them, taken as the reference.
    ///
    /// The reference is the active game window, or the first one in
    /// the configured order if there is none. This is what is
    /// expected in practice: a window is placed where wanted, and the
    /// others come onto it, at the same size. Anchor-based
    /// rearrangement keeps its role, in the grid of nine positions.
    /// </summary>
    /// <returns>Number of windows moved.</returns>
    public async Task<int> StackOnActiveAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default)
    {
        if (await StackTargetAsync(sessions, cancellationToken).ConfigureAwait(false)
            is not var (reference, rect))
        {
            return 0;
        }

        return await StackOnAsync(sessions, rect, reference, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The reference window of the stack and its rectangle, or
    /// <c>null</c> if there is nothing to stack.
    ///
    /// Published separately so the tabbed frame receives the same
    /// rectangle as the free windows: it is not a session, so it
    /// cannot appear in the list, and stacking it anywhere else than
    /// them would be anything but a rearrangement.
    /// </summary>
    public async Task<(ScrcpySession Reference, ScreenRect Rect)?> StackTargetAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var alive = sessions.Where(s => s.IsAlive).ToList();

        if (alive.Count == 0)
        {
            return null;
        }

        var foreground = _controller.GetForegroundWindow();

        // The one in the foreground, otherwise the last one to have
        // been, otherwise the first in the list, in the order chosen
        // by the user.
        var reference = alive.Find(s => s.WindowHandle != 0 && s.WindowHandle == foreground)
            ?? alive.Find(s => string.Equals(s.Id, _lastActive, StringComparison.Ordinal))
            ?? alive[0];

        var handle = await ResolveWindowAsync(reference, cancellationToken).ConfigureAwait(false);

        return handle != 0 && _controller.GetWindowRect(handle) is { } rect && !rect.IsEmpty
            ? (reference, rect)
            : null;
    }

    /// <summary>Places all the windows onto the same rectangle.</summary>
    /// <param name="except">
    /// The one that already occupies it, and is not counted.
    /// </param>
    /// <returns>Number of windows moved.</returns>
    public async Task<int> StackOnAsync(
        IReadOnlyList<ScrcpySession> sessions,
        ScreenRect rect,
        ScrcpySession? except = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var moved = 0;

        foreach (var session in sessions.Where(
            s => s.IsAlive && !ReferenceEquals(s, except)))
        {
            var other = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

            if (other == 0)
            {
                continue;
            }

            _controller.SetBorderless(other, IsFullscreen);
            _controller.MoveWindow(other, rect);
            _lastSeen[session.Id] = rect;
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// Places two windows side by side, each on one half of the
    /// screen.
    ///
    /// The active window goes to the right, the one that follows it
    /// in order to the left. The next ones stack behind the one on
    /// the left: beyond two, the screen no longer splits usefully.
    ///
    /// The height follows the display's aspect ratio: filling it
    /// further would leave a band, since the image is scaled.
    /// </summary>
    /// <param name="leaveRightFree">
    /// True when the right half belongs to someone else, the tabbed
    /// frame being in the foreground: all the windows then move to
    /// the left. Without this, a game window would land on the
    /// right on top of the frame, which is not a session and
    /// therefore cannot appear here.
    /// </param>
    /// <returns>Number of windows placed.</returns>
    public async Task<int> TileAsync(
        IReadOnlyList<ScrcpySession> sessions,
        bool leaveRightFree = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var alive = sessions.Where(s => s.IsAlive).ToList();
        var monitors = _controller.GetMonitors();

        if (alive.Count == 0 || monitors.Count == 0)
        {
            return 0;
        }

        var foreground = _controller.GetForegroundWindow();

        var right = leaveRightFree
            ? null
            : alive.Find(s => s.WindowHandle != 0 && s.WindowHandle == foreground)
                ?? alive.Find(s => string.Equals(s.Id, _lastActive, StringComparison.Ordinal))
                ?? alive[0];

        var rightHandle = right is null
            ? 0
            : await ResolveWindowAsync(right, cancellationToken).ConfigureAwait(false);

        var reference = rightHandle != 0 && _controller.GetWindowRect(rightHandle) is { } known && !known.IsEmpty
            ? WindowLayoutCalculator.ChooseMonitor(monitors, known.CenterX, known.CenterY)
            : WindowLayoutCalculator.ChooseMonitor(monitors, preferredDeviceName: null);

        var work = UsableArea(reference);

        var placed = 0;
        nint left = 0;

        foreach (var session in alive)
        {
            var handle = ReferenceEquals(session, right)
                ? rightHandle
                : await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

            if (handle == 0)
            {
                continue;
            }

            _controller.SetBorderless(handle, borderless: false);

            var rect = TileLayout.Half(
                work,
                ReferenceEquals(session, right),
                session.SourceAspectRatio,
                MeasureChrome(handle));

            _controller.MoveWindow(handle, rect);
            _lastSeen[session.Id] = rect;
            placed++;

            if (!ReferenceEquals(session, right))
            {
                left = handle;
            }
        }

        // The keyboard goes back to the left window: the right one
        // was already the one just left, and arranging it only to
        // stay there right away served no purpose.
        if (left != 0)
        {
            _controller.Focus(left);
        }

        return placed;
    }

    /// <summary>
    /// Remembers which game window is in the foreground.
    ///
    /// Called continuously: by the time of the rearrangement it is
    /// too late, the configurator having taken the foreground.
    /// </summary>
    public void TrackActiveWindow(IReadOnlyList<ScrcpySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var foreground = _controller.GetForegroundWindow();

        if (foreground == 0)
        {
            return;
        }

        if (sessions.FirstOrDefault(s => s.IsAlive && s.WindowHandle == foreground) is { } active)
        {
            _lastActive = active.Id;
        }
    }

    /// <summary>
    /// Places a window without touching its size.
    ///
    /// Used to park it off screen before the game opens: resizing it
    /// at that moment would make it come to life small, and it would
    /// never draw beyond that again.
    /// </summary>
    public async Task MoveOnlyAsync(
        ScrcpySession session,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

        if (handle == 0 || _controller.GetWindowRect(handle) is not { } rect || rect.IsEmpty)
        {
            return;
        }

        // A window already at the right corner is not moved: it was
        // this pointless move that could be seen jumping right after
        // opening.
        if (rect.X == x && rect.Y == y)
        {
            return;
        }

        _controller.MoveWindow(handle, rect with { X = x, Y = y });
    }

    /// <summary>
    /// Makes the window order follow the list order, and therefore
    /// the Alt+Tab order.
    ///
    /// The windows are raised from the last to the first: each one
    /// passes above the previous ones, so that the first in the list
    /// ends up on top. None of them takes focus, the window being
    /// played in stays the window being played in.
    ///
    /// The order of the taskbar thumbnails, however, follows creation
    /// order and does not move: Windows does not expose it.
    /// </summary>
    public async Task<int> ApplyOrderAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var handles = new List<nint>(sessions.Count);

        foreach (var session in sessions)
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

            if (handle != 0)
            {
                handles.Add(handle);
            }
        }

        for (var i = handles.Count - 1; i >= 0; i--)
        {
            _controller.Raise(handles[i]);
        }

        return handles.Count;
    }

    /// <summary>
    /// Asks a session's window to close itself, without waiting.
    /// Nothing happens if the window was never found.
    /// </summary>
    public void RequestClose(ScrcpySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.WindowHandle != 0)
        {
            _controller.RequestClose(session.WindowHandle);
        }
    }

    /// <summary>Process that owns a window, or zero.</summary>
    public int GetWindowProcessId(nint handle) => _controller.GetWindowProcessId(handle);

    /// <summary>Moves to the next instance, cyclically.</summary>
    public ScrcpySession? FocusNext(IReadOnlyList<ScrcpySession> sessions) => Cycle(sessions, forward: true);

    /// <summary>Moves back to the previous instance, cyclically.</summary>
    public ScrcpySession? FocusPrevious(IReadOnlyList<ScrcpySession> sessions) => Cycle(sessions, forward: false);

    /// <summary>Brings a specific session to the foreground.</summary>
    public bool Focus(ScrcpySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.WindowHandle == 0 || !_controller.IsWindow(session.WindowHandle))
        {
            return false;
        }

        _controller.Focus(session.WindowHandle);
        return true;
    }

    /// <summary>
    /// True if the active window belongs to one of the managed
    /// sessions. The window shortcuts only apply in that case,
    /// otherwise Ctrl+Tab would be hijacked in other software.
    /// </summary>
    public bool IsManagedWindowFocused(IReadOnlyList<ScrcpySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var foreground = _controller.GetForegroundWindow();

        return foreground != 0 && sessions.Any(s => s.IsAlive && s.WindowHandle == foreground);
    }

    private ScrcpySession? Cycle(IReadOnlyList<ScrcpySession> sessions, bool forward)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var alive = sessions.Where(s => s.IsAlive && s.WindowHandle != 0).ToList();
        if (alive.Count == 0)
        {
            _focusIndex = -1;
            return null;
        }

        // We start again from the actually active window, not from
        // an internal counter: the user may have switched windows
        // with the mouse.
        var foreground = _controller.GetForegroundWindow();
        var current = alive.FindIndex(s => s.WindowHandle == foreground);
        var from = current >= 0 ? current : _focusIndex;

        _focusIndex = forward
            ? WindowLayoutCalculator.NextIndex(alive.Count, from)
            : WindowLayoutCalculator.PreviousIndex(alive.Count, from < 0 ? 0 : from);

        var next = alive[_focusIndex];
        _controller.Focus(next.WindowHandle);

        return next;
    }
}

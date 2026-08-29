using DtHub.Core.Scrcpy;

namespace DtHub.Core.Windows;

/// <summary>
/// Place les fenêtres de jeu. Toutes reçoivent exactement le même rectangle et
/// se superposent donc parfaitement : on passe de l'une à l'autre au clavier
/// sans que rien ne bouge à l'écran.
/// </summary>
public sealed class WindowManagerService
{
    private readonly IWindowController _controller;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private int _focusIndex = -1;

    public WindowManagerService(
        IWindowController controller,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _controller = controller;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>Position du bloc de fenêtres dans l'écran.</summary>
    public WindowAnchor Anchor { get; set; } = WindowAnchor.MiddleLeft;

    /// <summary>Taille des fenêtres, en pourcentage de la zone utilisable.</summary>
    public int SizePercent { get; set; } = 70;

    /// <summary>Écran choisi dans les réglages, <c>null</c> pour l'écran principal.</summary>
    public string? PreferredMonitorDeviceName { get; set; }

    /// <summary>
    /// Délai maximal d'attente de la fenêtre scrcpy. Elle n'apparaît qu'une
    /// fois la première image reçue, ce qui prend un instant.
    /// </summary>
    public TimeSpan WindowAppearanceTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan WindowPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Écrans disponibles, pour les réglages.</summary>
    public IReadOnlyList<MonitorInfo> GetMonitors() => _controller.GetMonitors();

    /// <summary>
    /// Rectangle qu'occuperont les fenêtres de jeu, sans rien déplacer. Sert à
    /// poser le configurateur ailleurs.
    /// </summary>
    public ScreenRect? PreviewGameArea(double sourceAspectRatio)
    {
        var monitors = _controller.GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, PreferredMonitorDeviceName);

        return WindowLayoutCalculator.Calculate(monitor, SizePercent, sourceAspectRatio, Anchor);
    }

    /// <summary>Zone utilisable de l'écran retenu.</summary>
    public ScreenRect? WorkArea()
    {
        var monitors = _controller.GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, PreferredMonitorDeviceName);

        return monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
    }

    /// <summary>
    /// Retrouve la fenêtre d'une session. L'identifiant de session figure dans
    /// le titre, et le processus est vérifié : deux critères valent mieux
    /// qu'un pour ne pas déplacer la fenêtre d'un autre logiciel.
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

            var match = _controller.FindWindows(session.ProcessId)
                .FirstOrDefault(w => w.Title.Contains(session.Id, StringComparison.Ordinal));

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
    /// Place toutes les fenêtres au même endroit, à la position et à la taille
    /// configurées.
    /// </summary>
    /// <returns>Nombre de fenêtres effectivement déplacées.</returns>
    public async Task<int> ArrangeAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var monitors = _controller.GetMonitors();
        if (monitors.Count == 0)
        {
            return 0;
        }

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, PreferredMonitorDeviceName);
        var moved = 0;

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);
            if (handle == 0)
            {
                continue;
            }

            var rect = WindowLayoutCalculator.Calculate(
                monitor, SizePercent, session.SourceAspectRatio, Anchor);

            _controller.MoveWindow(handle, rect);
            moved++;
        }

        return moved;
    }

    /// <summary>Passe à l'instance suivante, en boucle.</summary>
    public ScrcpySession? FocusNext(IReadOnlyList<ScrcpySession> sessions) => Cycle(sessions, forward: true);

    /// <summary>Revient à l'instance précédente, en boucle.</summary>
    public ScrcpySession? FocusPrevious(IReadOnlyList<ScrcpySession> sessions) => Cycle(sessions, forward: false);

    /// <summary>Met une session précise au premier plan.</summary>
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
    /// Vrai si la fenêtre active appartient à l'une des sessions gérées. Les
    /// raccourcis de fenêtre ne s'appliquent que dans ce cas, sinon Ctrl+Tab
    /// serait détourné dans les autres logiciels.
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

        // On repart de la fenêtre réellement active, et non d'un compteur
        // interne : l'utilisateur a pu changer de fenêtre à la souris.
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

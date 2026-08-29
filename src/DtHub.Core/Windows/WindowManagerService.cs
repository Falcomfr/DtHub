using DtHub.Core.Scrcpy;

namespace DtHub.Core.Windows;

/// <summary>
/// Dispose les fenêtres des sessions. Le mode par défaut empile toutes les
/// fenêtres exactement au même endroit, à la même taille : on passe de l'une à
/// l'autre au clavier sans que rien ne bouge à l'écran.
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

    /// <summary>Tailles configurées.</summary>
    public WindowSizePresets Presets { get; set; } = WindowSizePresets.Default;

    /// <summary>Écran choisi dans les paramètres, <c>null</c> pour l'écran principal.</summary>
    public string? PreferredMonitorDeviceName { get; set; }

    /// <summary>Taille actuellement appliquée.</summary>
    public int CurrentSizeIndex { get; private set; } = 2;

    /// <summary>
    /// Délai maximal d'attente de la fenêtre scrcpy. Elle n'apparaît qu'une
    /// fois la première image reçue, ce qui prend un instant.
    /// </summary>
    public TimeSpan WindowAppearanceTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan WindowPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Retrouve la fenêtre d'une session. L'identifiant de session figure dans
    /// le titre, et le processus est vérifié : deux critères valent mieux qu'un
    /// pour ne pas déplacer la fenêtre d'un autre logiciel.
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
    /// Applique une taille à toutes les sessions et les empile. Toutes
    /// reçoivent exactement le même rectangle : c'est ce qui rend le passage
    /// d'une session à l'autre invisible.
    /// </summary>
    /// <returns>Nombre de fenêtres effectivement déplacées.</returns>
    public async Task<int> ApplySizeAsync(
        IReadOnlyList<ScrcpySession> sessions,
        int sizeIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        CurrentSizeIndex = Math.Clamp(sizeIndex, 0, Math.Max(0, Presets.Count - 1));

        var monitors = _controller.GetMonitors();
        if (monitors.Count == 0)
        {
            return 0;
        }

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, PreferredMonitorDeviceName);
        var fullscreen = Presets.IsFullscreen(CurrentSizeIndex);
        var moved = 0;

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);
            if (handle == 0)
            {
                continue;
            }

            var rect = WindowLayoutCalculator.Calculate(
                monitor, Presets, CurrentSizeIndex, session.SourceAspectRatio);

            // La bordure est retirée seulement en plein écran, et rétablie
            // dès qu'on en sort.
            _controller.SetBorderless(handle, fullscreen);
            _controller.MoveWindow(handle, rect);
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// Remet toutes les fenêtres ensemble, à la taille courante. C'est la
    /// commande de secours après avoir déplacé des fenêtres à la main.
    /// </summary>
    public Task<int> RecenterAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default) =>
        ApplySizeAsync(sessions, CurrentSizeIndex, cancellationToken);

    /// <summary>
    /// Passe à la session suivante, en boucle. Fonctionne pour deux sessions
    /// comme pour dix.
    /// </summary>
    /// <returns>La session mise au premier plan, ou <c>null</c> s'il n'y en a aucune.</returns>
    public ScrcpySession? FocusNext(IReadOnlyList<ScrcpySession> sessions)
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

        _focusIndex = WindowLayoutCalculator.NextIndex(alive.Count, current >= 0 ? current : _focusIndex);

        var next = alive[_focusIndex];
        _controller.Focus(next.WindowHandle);

        return next;
    }

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
    /// raccourcis de fenêtre ne doivent agir que dans ce cas, sinon Ctrl+Tab
    /// serait détourné dans les autres logiciels.
    /// </summary>
    public bool IsManagedWindowFocused(IReadOnlyList<ScrcpySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var foreground = _controller.GetForegroundWindow();

        return foreground != 0
               && sessions.Any(s => s.IsAlive && s.WindowHandle == foreground);
    }
}

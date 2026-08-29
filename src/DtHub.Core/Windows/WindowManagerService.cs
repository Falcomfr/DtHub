using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;

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

    /// <summary>Dernière taille vue par session, pour ne corriger qu'une fois le geste fini.</summary>
    private readonly Dictionary<string, ScreenRect> _lastSeen = new(StringComparer.Ordinal);


    public WindowManagerService(
        IWindowController controller,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _controller = controller;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>Position du bloc de fenêtres dans l'écran.</summary>
    public WindowAnchor Anchor { get; set; } = WindowAnchor.MiddleLeft;

    /// <summary>Tailles configurées, proportionnelles à l'écran.</summary>
    public WindowSizePresets Presets { get; set; } = WindowSizePresets.Default;

    /// <summary>Taille en cours, par son indice dans les tailles configurées.</summary>
    public int SizeIndex { get; private set; } = 1;

    /// <summary>
    /// Taille choisie librement au curseur, en pourcentage de la zone
    /// utilisable. Elle l'emporte sur l'indice tant qu'un raccourci de taille
    /// n'a pas été employé : le curseur est une valeur continue, les
    /// raccourcis quatre repères sur cette même échelle.
    /// </summary>
    public int? CustomSizePercent { get; private set; }

    /// <summary>Taille en cours, en pourcentage de la zone utilisable.</summary>
    public int SizePercent => CustomSizePercent ?? Presets.PercentageAt(SizeIndex);

    /// <summary>Vrai si la taille en cours est le plein écran sans bordure.</summary>
    public bool IsFullscreen => CustomSizePercent is null && Presets.IsFullscreen(SizeIndex);

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

        return Compute(monitor, sourceAspectRatio, (0, 0));
    }

    /// <summary>
    /// Rectangle d'une fenêtre sur un écran donné. Le plein écran couvre
    /// l'écran entier, barre des tâches comprise.
    /// </summary>
    /// <param name="chrome">
    /// Encombrement de la barre de titre et des bordures, mesuré sur la
    /// fenêtre. Le rapport d'affichage s'applique à la zone client, celle que
    /// scrcpy remplit : l'ignorer laisse des bandes noires sur les côtés.
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
    /// Taille d'une fenêtre, sans décider de sa place. Le rapport s'applique à
    /// la zone client, celle que scrcpy remplit.
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
    /// Change la taille des fenêtres sans les déplacer, en multipliant la
    /// taille de chacune par le même facteur.
    ///
    /// Leur donner à toutes la même taille effacerait les écarts voulus : une
    /// fenêtre volontairement plus petite qu'une autre doit le rester. Un
    /// raccourci de taille ou le curseur ne demandent qu'à agrandir ou
    /// réduire, pas à uniformiser ni à replacer. Seul le plein écran couvre
    /// l'écran entier.
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
            var target = WindowLayoutCalculator.ClampInto(
                current with
                {
                    Width = Math.Max(120, (int)Math.Round(current.Width * factor)),
                    Height = Math.Max(80, (int)Math.Round(current.Height * factor)),
                },
                UsableArea(monitor));

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
    /// Ramène les fenêtres à la forme de leur afficheur, au rapport verrouillé.
    ///
    /// Ce mode met l'image à l'échelle : elle ne remplit la fenêtre qu'à cette
    /// forme, et s'en écarter laisse une bande. En largeur libre, rien n'est
    /// corrigé : redimensionner y est libre dans les deux sens.
    /// </summary>
    /// <returns>Nombre de fenêtres corrigées.</returns>
    public int EnforceAspect(IReadOnlyList<ScrcpySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (IsFullscreen)
        {
            return 0;
        }

        var corrected = 0;

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

            var chrome = MeasureChrome(session.WindowHandle);

            // Rapport verrouillé : l'image est mise à l'échelle, elle ne
            // remplit la fenêtre qu'à la forme de l'afficheur. S'en écarter
            // laisse une bande, au-dessus comme en dessous.
            var wanted = (int)Math.Round(
                (outer.Width - chrome.Width) / session.SourceAspectRatio) + chrome.Height;

            if (Math.Abs(outer.Height - wanted) <= 2)
            {
                continue;
            }

            var target = outer with { Height = wanted };

            _controller.MoveWindow(session.WindowHandle, target);
            _lastSeen[session.Id] = target;
            corrected++;
        }

        return corrected;
    }

    /// <summary>
    /// Différence entre le rectangle extérieur d'une fenêtre et sa zone
    /// client. Nulle si la mesure échoue, auquel cas le calcul retombe sur le
    /// comportement d'avant.
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
    /// Applique une taille à toutes les fenêtres et les replace. L'indice hors
    /// bornes est ramené dans les limites plutôt que refusé.
    /// </summary>
    public Task<int> ApplySizeAsync(
        IReadOnlyList<ScrcpySession> sessions,
        int sizeIndex,
        CancellationToken cancellationToken = default)
    {
        var previous = SizePercent;

        SizeIndex = Math.Clamp(sizeIndex, 0, Math.Max(0, Presets.Count - 1));

        // Un raccourci de taille reprend la main sur le curseur.
        CustomSizePercent = null;

        return ScaleInPlaceAsync(sessions, (double)SizePercent / previous, cancellationToken);
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
    /// Retrouve la fenêtre d'une session par son processus. Chaque processus
    /// scrcpy n'ouvre qu'une fenêtre visible, ce qui suffit à l'identifier et
    /// laisse le titre entièrement au nom choisi par l'utilisateur. Le titre
    /// ne sert que de départage si plusieurs fenêtres apparaissaient.
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
    /// Place toutes les fenêtres au même endroit, à la position et à la taille
    /// configurées. C'est le replacement rapide : il écrase délibérément la
    /// géométrie que l'utilisateur avait donnée à chaque fenêtre.
    /// </summary>
    /// <returns>Nombre de fenêtres effectivement déplacées.</returns>
    public async Task<int> ArrangeAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default)
    {
        var applied = await ApplyLayoutAsync(sessions, remembered: null, cancellationToken)
            .ConfigureAwait(false);

        return applied.Count;
    }

    /// <summary>
    /// Place chaque fenêtre là où elle avait été laissée, et retombe sur le
    /// placement calculé pour celles qui n'ont pas encore de géométrie.
    /// </summary>
    /// <returns>Les rectangles réellement appliqués, par clé d'instance.</returns>
    public Task<IReadOnlyList<(string Key, ScreenRect Rect)>> RestoreAsync(
        IReadOnlyList<ScrcpySession> sessions,
        IReadOnlyDictionary<string, StoredWindowRect> remembered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remembered);

        return ApplyLayoutAsync(sessions, remembered, cancellationToken);
    }

    /// <summary>
    /// Géométrie actuelle de chaque fenêtre vivante, par clé d'instance.
    ///
    /// Le plein écran est écarté : son rectangle vaut l'écran entier et la
    /// fenêtre y est sans bordure. Le mémoriser puis le restaurer en taille
    /// normale donnerait une fenêtre bordée débordant sous la barre des
    /// tâches. Une fenêtre réduite est écartée aussi, son rectangle ne voulant
    /// rien dire.
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
    /// Applique une disposition. Sans géométries mémorisées, toutes les
    /// fenêtres reçoivent le rectangle calculé depuis l'ancrage.
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

        var monitor = WindowLayoutCalculator.ChooseMonitor(monitors, PreferredMonitorDeviceName);
        List<(string, ScreenRect)> applied = [];

        foreach (var session in sessions.Where(s => s.IsAlive))
        {
            var handle = await ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);
            if (handle == 0)
            {
                continue;
            }

            // La bordure ne disparaît qu'en plein écran, et revient en sortant.
            _controller.SetBorderless(handle, IsFullscreen);

            (int Width, int Height) chrome = IsFullscreen ? (0, 0) : MeasureChrome(handle);
            var rect = Resolve(session, monitor, monitors, chrome, remembered);

            _controller.MoveWindow(handle, rect);

            applied.Add((session.Target.Key, rect));
        }

        return applied;
    }

    /// <summary>
    /// Rectangle d'une fenêtre : celui qu'elle avait quand il vaut encore, le
    /// calcul par ancrage sinon. Le plein écran l'emporte toujours sur une
    /// géométrie mémorisée.
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
            return Fit(session, restored, chrome);
        }

        return Fit(session, Compute(monitor, session.SourceAspectRatio, chrome), chrome);
    }

    /// <summary>
    /// Ramène un rectangle que nous calculons à la hauteur que le jeu sait
    /// dessiner.
    ///
    /// La retenir ici, et là seulement, évite d'ouvrir la fenêtre trop haute
    /// pour la rapetisser aussitôt. Une fenêtre agrandie à la main n'y passe
    /// pas : ce que l'utilisateur a fait de ses mains n'est jamais défait.
    /// </summary>
    private static ScreenRect Fit(
        ScrcpySession session, ScreenRect rect, (int Width, int Height) chrome)
    {
        if (session.MaxClientHeight <= 0)
        {
            return rect;
        }

        var ceiling = session.MaxClientHeight + chrome.Height;

        return rect.Height <= ceiling ? rect : rect with { Height = ceiling };
    }

    /// <summary>
    /// Applique la taille libre du curseur, sans déplacer les fenêtres.
    /// </summary>
    public Task<int> ApplyPercentAsync(
        IReadOnlyList<ScrcpySession> sessions,
        int percent,
        CancellationToken cancellationToken = default)
    {
        var previous = SizePercent;

        CustomSizePercent = Math.Clamp(percent, 20, 100);

        return ScaleInPlaceAsync(sessions, (double)SizePercent / previous, cancellationToken);
    }

    /// <summary>
    /// Réécrit le titre de chaque fenêtre ouverte. scrcpy ne fixe le sien
    /// qu'au démarrage : sans cela, le rappel du raccourci resterait périmé
    /// jusqu'à la prochaine ouverture.
    /// </summary>
    /// <returns>Nombre de fenêtres renommées.</returns>
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
    /// Empile les fenêtres sur l'une d'elles, prise comme référence.
    ///
    /// La référence est la fenêtre de jeu active, ou la première dans l'ordre
    /// configuré s'il n'y en a pas. C'est ce qu'on attend en pratique : on
    /// place une fenêtre là où on la veut, et les autres viennent dessus, à
    /// la même taille. Le replacement par ancrage garde son rôle, dans la
    /// grille des neuf positions.
    /// </summary>
    /// <returns>Nombre de fenêtres déplacées.</returns>
    public async Task<int> StackOnActiveAsync(
        IReadOnlyList<ScrcpySession> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var alive = sessions.Where(s => s.IsAlive).ToList();

        if (alive.Count == 0)
        {
            return 0;
        }

        var foreground = _controller.GetForegroundWindow();

        var reference = alive.Find(s => s.WindowHandle != 0 && s.WindowHandle == foreground) ?? alive[0];

        var handle = await ResolveWindowAsync(reference, cancellationToken).ConfigureAwait(false);

        if (handle == 0 || _controller.GetWindowRect(handle) is not { } rect || rect.IsEmpty)
        {
            return 0;
        }

        var moved = 0;

        foreach (var session in alive.Where(s => !ReferenceEquals(s, reference)))
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
    /// Place une fenêtre sans toucher à sa taille.
    ///
    /// Sert avant l'ouverture du jeu : l'afficheur est alors à sa hauteur
    /// maximale, et le réduire tout de suite ferait naître le jeu petit, sans
    /// possibilité de grandir ensuite.
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

        _controller.MoveWindow(handle, rect with { X = x, Y = y });
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

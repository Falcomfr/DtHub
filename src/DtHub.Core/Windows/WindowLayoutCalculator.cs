using DtHub.Core.Localization;

namespace DtHub.Core.Windows;

/// <summary>
/// Calcule la position et la taille des fenêtres. Fonctions pures : la
/// disposition se vérifie entièrement sans écran ni fenêtre réelle.
/// </summary>
public static class WindowLayoutCalculator
{
    /// <summary>
    /// Rectangle d'une fenêtre de jeu : une part de la zone utilisable,
    /// au rapport d'affichage de l'écran virtuel Android, collée à la
    /// position demandée.
    /// </summary>
    /// <param name="monitor">Écran visé.</param>
    /// <param name="sizePercent">Part de la zone utilisable, en pourcentage.</param>
    /// <param name="sourceAspectRatio">
    /// Rapport largeur sur hauteur de la source. Zéro remplit sans contrainte
    /// de forme.
    /// </param>
    /// <param name="anchor">Position dans la grille.</param>
    public static ScreenRect Calculate(
        MonitorInfo monitor,
        int sizePercent,
        double sourceAspectRatio,
        WindowAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        var fraction = Math.Clamp(sizePercent, 20, 100) / 100.0;

        var maxWidth = Math.Max(1, (int)Math.Round(work.Width * fraction));
        var maxHeight = Math.Max(1, (int)Math.Round(work.Height * fraction));

        var (width, height) = sourceAspectRatio > 0
            ? FitToAspect(maxWidth, maxHeight, sourceAspectRatio)
            : (maxWidth, maxHeight);

        return Place(work, width, height, anchor);
    }

    /// <summary>
    /// Colle un rectangle de taille donnée à une position de la grille, à
    /// l'intérieur d'une zone.
    /// </summary>
    public static ScreenRect Place(ScreenRect area, int width, int height, WindowAnchor anchor)
    {
        var w = Math.Clamp(width, 1, Math.Max(1, area.Width));
        var h = Math.Clamp(height, 1, Math.Max(1, area.Height));

        var left = area.X;
        var centerX = area.X + ((area.Width - w) / 2);
        var right = area.Right - w;

        var top = area.Y;
        var middleY = area.Y + ((area.Height - h) / 2);
        var bottom = area.Bottom - h;

        var (x, y) = anchor switch
        {
            WindowAnchor.TopLeft => (left, top),
            WindowAnchor.TopCenter => (centerX, top),
            WindowAnchor.TopRight => (right, top),
            WindowAnchor.MiddleLeft => (left, middleY),
            WindowAnchor.Center => (centerX, middleY),
            WindowAnchor.MiddleRight => (right, middleY),
            WindowAnchor.BottomLeft => (left, bottom),
            WindowAnchor.BottomCenter => (centerX, bottom),
            WindowAnchor.BottomRight => (right, bottom),
            _ => (centerX, middleY),
        };

        return new ScreenRect(x, y, w, h);
    }

    /// <summary>
    /// Plus grande taille respectant le rapport demandé et tenant dans les
    /// bornes fournies.
    /// </summary>
    public static (int Width, int Height) FitToAspect(int maxWidth, int maxHeight, double aspectRatio)
    {
        if (aspectRatio <= 0)
        {
            return (maxWidth, maxHeight);
        }

        var widthFromHeight = (int)Math.Round(maxHeight * aspectRatio);

        return widthFromHeight <= maxWidth
            ? (Math.Max(1, widthFromHeight), maxHeight)
            : (maxWidth, Math.Max(1, (int)Math.Round(maxWidth / aspectRatio)));
    }

    /// <summary>
    /// Écran désigné par son nom dans les réglages, avec repli sur l'écran
    /// principal si celui qui était choisi a été débranché.
    /// </summary>
    public static MonitorInfo ChooseMonitor(IReadOnlyList<MonitorInfo> monitors, string? preferredDeviceName)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException(Strings.Get("NoScreenDetected"));
        }

        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            var preferred = monitors.FirstOrDefault(
                m => string.Equals(m.DeviceName, preferredDeviceName, StringComparison.Ordinal));

            if (preferred is not null)
            {
                return preferred;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>Écran contenant le point donné, ou l'écran principal à défaut.</summary>
    public static MonitorInfo ChooseMonitor(IReadOnlyList<MonitorInfo> monitors, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException(Strings.Get("NoScreenDetected"));
        }

        return monitors.FirstOrDefault(m => m.Bounds.Contains(x, y))
               ?? monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors[0];
    }

    /// <summary>Rectangle transposé d'un écran à un autre, proportionnellement.</summary>
    public static ScreenRect Rescale(ScreenRect rect, ScreenRect from, ScreenRect to)
    {
        if (from.IsEmpty || to.IsEmpty)
        {
            return rect;
        }

        var scaleX = (double)to.Width / from.Width;
        var scaleY = (double)to.Height / from.Height;

        return new ScreenRect(
            to.X + (int)Math.Round((rect.X - from.X) * scaleX),
            to.Y + (int)Math.Round((rect.Y - from.Y) * scaleY),
            Math.Max(1, (int)Math.Round(rect.Width * scaleX)),
            Math.Max(1, (int)Math.Round(rect.Height * scaleY)));
    }

    /// <summary>
    /// Ramène un rectangle entièrement dans une zone, en le rétrécissant s'il
    /// est trop grand pour y tenir.
    /// </summary>
    public static ScreenRect ClampInto(ScreenRect rect, ScreenRect area)
    {
        if (area.IsEmpty)
        {
            return rect;
        }

        var width = Math.Min(Math.Max(1, rect.Width), area.Width);
        var height = Math.Min(Math.Max(1, rect.Height), area.Height);

        return new ScreenRect(
            Math.Clamp(rect.X, area.X, area.Right - width),
            Math.Clamp(rect.Y, area.Y, area.Bottom - height),
            width,
            height);
    }

    /// <summary>
    /// Rectangle utilisable pour une géométrie mémorisée, ou <c>null</c> quand
    /// rien de sensé ne peut en être tiré : l'appelant retombe alors sur le
    /// placement calculé depuis l'ancrage.
    ///
    /// L'écran est reconnu par ses bornes autant que par son nom, car ce nom
    /// est positionnel : débrancher un écran renumérote les suivants, et une
    /// fenêtre se retrouverait restaurée sur le mauvais.
    /// </summary>
    public static ScreenRect? RestoreRemembered(
        ScreenRect remembered,
        string? monitorDeviceName,
        ScreenRect monitorBounds,
        IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (remembered.IsEmpty || monitors.Count == 0)
        {
            return null;
        }

        var named = monitors.FirstOrDefault(
            m => string.Equals(m.DeviceName, monitorDeviceName, StringComparison.Ordinal));

        // Même écran, mêmes bornes : le rectangle vaut encore, à condition de
        // tomber réellement dessus. Une fenêtre qui était réduite au moment de
        // la capture rend un rectangle en (-32000, -32000), qu'il ne faut
        // surtout pas restaurer.
        if (named is not null && named.Bounds == monitorBounds && IsMostlyOn(remembered, named))
        {
            return remembered;
        }

        // Définition ou disposition changée : on transpose proportionnellement.
        if (named is not null && named.Bounds != monitorBounds && !monitorBounds.IsEmpty)
        {
            return ClampInto(Rescale(remembered, monitorBounds, named.Bounds), UsableArea(named));
        }

        // L'écran d'origine a disparu ou changé de rang. Si un écran porte
        // encore l'essentiel de la fenêtre, elle y reste.
        var host = monitors
            .OrderByDescending(m => m.Bounds.Intersect(remembered).Area)
            .First();

        return IsMostlyOn(remembered, host) ? ClampInto(remembered, UsableArea(host)) : null;
    }

    /// <summary>Vrai si au moins la moitié du rectangle tombe sur cet écran.</summary>
    private static bool IsMostlyOn(ScreenRect rect, MonitorInfo monitor) =>
        monitor.Bounds.Intersect(rect).Area * 2 >= rect.Area;

    private static ScreenRect UsableArea(MonitorInfo monitor) =>
        monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;

    /// <summary>
    /// Indice de la session suivante, en boucle. Fonctionne pour deux
    /// instances comme pour dix, et repart du début quand la session courante
    /// n'est plus dans la liste.
    /// </summary>
    public static int NextIndex(int count, int currentIndex)
    {
        if (count <= 0)
        {
            return -1;
        }

        return currentIndex is < 0 or int.MaxValue || currentIndex >= count - 1
            ? 0
            : currentIndex + 1;
    }

    /// <summary>Indice de la session précédente, en boucle.</summary>
    public static int PreviousIndex(int count, int currentIndex)
    {
        if (count <= 0)
        {
            return -1;
        }

        return currentIndex <= 0 ? count - 1 : currentIndex - 1;
    }
}

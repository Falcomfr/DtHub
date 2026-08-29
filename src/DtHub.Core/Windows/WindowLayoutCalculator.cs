namespace DtHub.Core.Windows;

/// <summary>
/// Calcule la position et la taille des fenêtres. Fonctions pures : la
/// disposition se vérifie entièrement sans écran ni fenêtre réelle, ce qui
/// évite d'avoir à déplacer des fenêtres pour tester un calcul.
/// </summary>
public static class WindowLayoutCalculator
{
    /// <summary>
    /// Calcule le rectangle d'une fenêtre pour une taille donnée. Le rapport
    /// d'affichage vient de l'écran virtuel Android, pas d'une valeur figée :
    /// un téléphone est en portrait, une tablette en paysage.
    /// </summary>
    /// <param name="monitor">Écran visé.</param>
    /// <param name="presets">Tailles configurées.</param>
    /// <param name="index">Indice de taille demandé.</param>
    /// <param name="sourceAspectRatio">
    /// Rapport largeur sur hauteur de la source. Une valeur nulle ou négative
    /// fait remplir la zone disponible sans contrainte de forme.
    /// </param>
    public static ScreenRect Calculate(
        MonitorInfo monitor,
        WindowSizePresets presets,
        int index,
        double sourceAspectRatio)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(presets);

        if (presets.IsFullscreen(index))
        {
            // Plein écran sans bordure : l'écran entier, barre des tâches
            // comprise, sinon ce ne serait pas du plein écran.
            return monitor.Bounds;
        }

        var work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        var fraction = presets.PercentageAt(index) / 100.0;

        var maxWidth = Math.Max(1, (int)Math.Round(work.Width * fraction));
        var maxHeight = Math.Max(1, (int)Math.Round(work.Height * fraction));

        var (width, height) = sourceAspectRatio > 0
            ? FitToAspect(maxWidth, maxHeight, sourceAspectRatio)
            : (maxWidth, maxHeight);

        return Center(work, width, height);
    }

    /// <summary>
    /// Centre un rectangle de taille donnée dans une zone. Utilisé par la
    /// commande Recentrer, qui remet ensemble des fenêtres déplacées à la
    /// main.
    /// </summary>
    public static ScreenRect Center(ScreenRect area, int width, int height)
    {
        var clampedWidth = Math.Clamp(width, 1, Math.Max(1, area.Width));
        var clampedHeight = Math.Clamp(height, 1, Math.Max(1, area.Height));

        return new ScreenRect(
            area.X + ((area.Width - clampedWidth) / 2),
            area.Y + ((area.Height - clampedHeight) / 2),
            clampedWidth,
            clampedHeight);
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
    /// Écran contenant le point donné, ou l'écran principal à défaut. Sert à
    /// suivre la fenêtre que l'utilisateur vient de déplacer.
    /// </summary>
    public static MonitorInfo ChooseMonitor(
        IReadOnlyList<MonitorInfo> monitors,
        int x,
        int y)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Aucun écran n'a été détecté.");
        }

        return monitors.FirstOrDefault(m => m.Bounds.Contains(x, y))
               ?? monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors[0];
    }

    /// <summary>
    /// Écran désigné par son nom dans les paramètres, avec repli sur l'écran
    /// principal si celui qui était choisi a été débranché.
    /// </summary>
    public static MonitorInfo ChooseMonitor(IReadOnlyList<MonitorInfo> monitors, string? preferredDeviceName)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Aucun écran n'a été détecté.");
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

    /// <summary>
    /// Indice de la session suivante dans un parcours circulaire. Fonctionne
    /// pour deux sessions comme pour dix, et repart du début quand la session
    /// courante n'est plus dans la liste.
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
}

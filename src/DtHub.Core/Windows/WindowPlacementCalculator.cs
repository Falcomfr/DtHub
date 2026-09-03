namespace DtHub.Core.Windows;

/// <summary>
/// Décide si une place retenue peut être rendue à une fenêtre.
///
/// Une position enregistrée sur un écran depuis débranché enverrait la fenêtre
/// dans le vide : elle serait ouverte, présente dans la barre des tâches, et
/// invisible. Mieux vaut alors la laisser s'ouvrir à sa place par défaut.
///
/// Fonction pure : elle se vérifie sans écran ni fenêtre.
/// </summary>
public static class WindowPlacementCalculator
{
    /// <summary>
    /// Ce qu'il faut voir de la fenêtre pour la juger rattrapable, en pixels.
    /// Assez pour poser le curseur sur sa barre de titre et la ramener.
    /// </summary>
    public const int MinimumVisible = 120;

    /// <summary>
    /// Vrai si la place demandée laisse voir assez de la fenêtre sur l'un des
    /// écrans présents.
    /// </summary>
    public static bool IsReachable(
        WindowPlacement? placement,
        IReadOnlyList<ScreenRect> screens,
        int minimum = MinimumVisible)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (placement is not { IsSized: true })
        {
            return false;
        }

        var wanted = new ScreenRect(
            placement.Left,
            placement.Top,
            placement.Right - placement.Left,
            placement.Bottom - placement.Top);

        foreach (var screen in screens)
        {
            var shared = wanted.Intersect(screen);

            // Les deux dimensions comptent : une bande d'un pixel de haut sur
            // toute la largeur ne se saisit pas plus qu'un coin.
            if (shared.Width >= Math.Min(minimum, wanted.Width)
                && shared.Height >= Math.Min(minimum, wanted.Height))
            {
                return true;
            }
        }

        return false;
    }
}

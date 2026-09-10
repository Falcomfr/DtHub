namespace DtHub.Core.Sessions;

/// <summary>
/// Décide de ce qui paraît au démarrage.
///
/// Trois règles, qui tiennent ensemble : une fenêtre de jeu fermée à la main
/// revient au lancement suivant, une fenêtre fermée depuis le panneau n'y
/// revient pas, et le panneau se montre dès qu'il ne resterait rien à l'écran.
/// </summary>
public static class StartupPresence
{
    /// <summary>
    /// Vrai si le configurateur doit être affiché, sachant s'il l'était à la
    /// sortie et combien de fenêtres de jeu viennent de s'ouvrir.
    ///
    /// Sans fenêtre de jeu, il est tout ce qui reste : le masquer laisserait
    /// une application sans rien à l'écran, et sans même le rappel du raccourci
    /// que portent les titres des fenêtres. Cela vaut qu'un problème soit à
    /// montrer ou que l'ensemble de démarrage soit simplement vide, les deux
    /// menant au même écran vide.
    /// </summary>
    public static bool ShowConfigurator(bool remembered, int openedWindows) =>
        remembered || openedWindows <= 0;

    /// <summary>
    /// Vrai si le configurateur peut paraître sans attendre le lancement.
    ///
    /// Ouvrir les sessions demande plusieurs secondes, pendant lesquelles rien
    /// ne paraissait : l'application semblait ne pas démarrer, alors qu'elle
    /// travaillait. Le panneau est pourtant prêt bien avant elles.
    ///
    /// La seule chose qui empêchait de le montrer tout de suite est que sa
    /// présence dépend du nombre de fenêtres ouvertes, qu'on ne connaît qu'à la
    /// fin. Sauf dans un cas : quand il était affiché à la sortie, il reste
    /// affiché quoi qu'il arrive ensuite. Le montrer alors n'anticipe rien, et
    /// ne peut donc pas mener à le reprendre à l'écran.
    ///
    /// Autrement dit, cette règle ne rend vrai que là où
    /// <see cref="ShowConfigurator" /> rendra vrai de toute façon.
    /// </summary>
    /// <param name="remembered">Le panneau était-il affiché à la sortie.</param>
    public static bool ShowBeforeLaunch(bool remembered) => remembered;
}

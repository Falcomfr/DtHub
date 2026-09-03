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
}

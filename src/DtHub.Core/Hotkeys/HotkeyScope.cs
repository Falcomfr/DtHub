namespace DtHub.Core.Hotkeys;

/// <summary>Une fenêtre de session, par son handle et son processus.</summary>
public readonly record struct SessionWindow(nint Handle, int ProcessId);

/// <summary>
/// Décide si les raccourcis restent armés, selon la fenêtre au premier plan.
///
/// C'est la seule chose qui empêche <c>RegisterHotKey</c> d'être global, et le
/// README en fait une promesse : « only the combinations you configured are
/// ever intercepted, and only while a DT Hub window is focused ». La règle
/// vivait au milieu d'un gestionnaire d'événements asynchrone, sur un fil de
/// fond, sans aucune épreuve : une régression aurait confisqué Ctrl+Tab et
/// Ctrl+R à l'échelle du système, navigateur compris, sans que rien ne rougisse
/// et sans que personne puisse relier le symptôme à DT Hub.
/// </summary>
public static class HotkeyScope
{
    /// <summary>
    /// Vrai si la fenêtre au premier plan est à nous.
    ///
    /// Une session est reconnue par son handle ou par son processus. Le
    /// processus compte : le handle d'une session fraîchement rouverte n'est
    /// pas encore résolu, et les raccourcis se croyaient alors hors de chez eux
    /// jusqu'à ce qu'on clique ailleurs puis de nouveau sur une fenêtre de jeu.
    /// </summary>
    /// <param name="foreground">Handle de la fenêtre au premier plan.</param>
    /// <param name="owner">Processus qui la possède, ou zéro s'il est inconnu.</param>
    /// <param name="sessions">Les fenêtres de jeu ouvertes.</param>
    /// <param name="ours">
    /// Vrai si c'est une fenêtre de l'application elle-même : configurateur,
    /// guides, page liée, ou cadre à onglets.
    /// </param>
    public static bool Holds(
        nint foreground,
        int owner,
        IEnumerable<SessionWindow> sessions,
        bool ours)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (ours)
        {
            return true;
        }

        return sessions.Any(s => s.Handle == foreground || (owner != 0 && s.ProcessId == owner));
    }
}

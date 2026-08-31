using System.IO;
using System.Reflection;

namespace DtHub.App.Windows;

/// <summary>
/// Le script injecté dans les pages du site.
///
/// Il rend deux services que rien n'oblige à prendre ensemble : le cadrage, qui
/// retire le décor du site pour qu'une page tienne dans une fenêtre étroite, et
/// le suivi d'étapes, qui n'a de sens que sur un guide de quête. La fenêtre des
/// pages liées ne prend que le premier : ce qu'elle affiche est aussi bien une
/// rubrique qu'une carte, où il n'y a ni étape à décrire ni chaîne à suivre.
/// </summary>
internal static class QuestBridge
{
    /// <summary>
    /// Le pont, tel qu'on l'injecte avant la création du document.
    /// </summary>
    /// <param name="framingOnly">
    /// Vrai pour ne garder que le cadrage. Le titre de l'article est alors
    /// conservé : sur une page de rubrique c'est le seul repère, et cette
    /// fenêtre-là n'en réaffiche aucun dans son propre bandeau.
    /// </param>
    public static string Script(bool framingOnly = false)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DtHub.App.quest-bridge.js")
            ?? throw new InvalidOperationException("Le pont de la fenêtre de quêtes est absent de l'assembly.");

        using var reader = new StreamReader(stream);

        var source = reader.ReadToEnd();

        return framingOnly ? "window.__dtHubFramingOnly = true;\n" + source : source;
    }
}

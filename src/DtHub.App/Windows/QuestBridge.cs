using System.IO;
using System.Reflection;
using System.Text.Json;

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
        var source = Read("DtHub.App.quest-bridge.js");

        return framingOnly ? "window.__dtHubFramingOnly = true;\n" + source : source;
    }

    /// <summary>
    /// Le script qui prépare le formulaire de signalement du site.
    ///
    /// Il ne va pas avec le pont, il le remplace : le pont masque le pied
    /// d'article, et c'est justement là que le formulaire se trouve.
    ///
    /// Le repère d'étape passe par une chaîne JSON, produite par le
    /// sérialiseur : c'est aussi une chaîne JavaScript valide, et un texte de
    /// guide contient guillemets et apostrophes qu'il faudrait sinon échapper à
    /// la main, en oubliant un cas.
    /// </summary>
    public static string ReportScript(string? location) =>
        Read("DtHub.App.papycha-report.js")
            .Replace(
                "__DTHUB_LOCATION__",
                JsonSerializer.Serialize(location ?? string.Empty),
                StringComparison.Ordinal);

    private static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"La ressource « {name} » est absente de l'assembly.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

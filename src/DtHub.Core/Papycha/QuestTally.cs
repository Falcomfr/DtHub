using System.Globalization;

namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qu'on retient d'un catalogue pour dire, après relecture, ce qui a changé.
///
/// La règle vit ici et non dans la fenêtre : c'est une phrase que l'utilisateur
/// lit, avec ses accords et son silence, et rien de tout cela ne se vérifiait.
/// </summary>
public readonly record struct QuestTally(int Quests, int Places, int Paths)
{
    /// <summary>Le relevé d'un catalogue.</summary>
    public static QuestTally Of(QuestCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return new QuestTally(catalog.Quests.Count, catalog.Dungeons.Count, catalog.Paths.Count);
    }

    /// <summary>
    /// Ce qu'une relecture a rapporté depuis un relevé, en une ligne.
    ///
    /// Rien quand elle n'a rien changé : le site remanie souvent ses pages sans
    /// que le catalogue en gagne ou en perde, et l'annoncer à chaque fois serait
    /// du bruit. Rien non plus quand il n'y avait rien avant : annoncer « sept
    /// cent quatre-vingt-deux quêtes de plus » à la première lecture
    /// n'apprendrait rien.
    /// </summary>
    public string Since(QuestTally before)
    {
        if (before.Quests == 0 || before == this)
        {
            return string.Empty;
        }

        List<string> parts = [];

        Add(Quests - before.Quests, "quête", "quêtes");
        Add(Places - before.Places, "lieu de combat", "lieux de combat");
        Add(Paths - before.Paths, "chemin", "chemins");

        return parts.Count == 0
            ? string.Empty
            : "Guides relus : " + string.Join(", ", parts) + ".";

        void Add(int delta, string one, string many)
        {
            if (delta == 0)
            {
                return;
            }

            var count = Math.Abs(delta);

            parts.Add(
                $"{count.ToString(CultureInfo.CurrentCulture)} "
                + $"{(count > 1 ? many : one)} {(delta > 0 ? "de plus" : "de moins")}");
        }
    }
}

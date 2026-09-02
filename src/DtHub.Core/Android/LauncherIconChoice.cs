using System.Text.RegularExpressions;

namespace DtHub.Core.Android;

/// <summary>
/// Choisit, dans le listage d'une archive, l'entrée qui porte l'icône de
/// lancement.
///
/// Ni le manifeste ni <c>resources.arsc</c> ne sont lus : ce serait décoder le
/// format de ressources d'Android pour retrouver un nom que la convention
/// donne déjà. Le modèle d'Android nomme cette ressource « ic_launcher », et
/// c'est le cas du jeu visé, mesuré. Quand la convention ne tient pas, on ne
/// rend rien, ce qui vaut exactement l'état d'avant.
/// </summary>
public static partial class LauncherIconChoice
{
    /// <summary>
    /// Plafond de transfert. Cinquante et un kilooctets suffisent à la plus
    /// dense des icônes du jeu, mesuré ; le plafond n'est pas là pour elle mais
    /// pour l'archive inconnue qui logerait une image d'un mégaoctet sous ce
    /// nom.
    /// </summary>
    public const long MaximumBytes = 256 * 1024;

    /// <summary>
    /// L'entrée à extraire, ou <c>null</c> quand l'archive n'a pas d'icône
    /// matricielle. C'est le cas d'une application qui ne livre qu'une icône
    /// adaptative, décrite en XML et dessinée par le lanceur : la rendre
    /// demanderait un rasteriseur de vectoriels et un lecteur de ressources.
    /// </summary>
    public static string? Choose(IReadOnlyList<ApkEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var candidates = entries
            .Where(e => e.Length > 0 && e.Length <= MaximumBytes)
            .Select(e => (Entry: e, Match: IconPattern().Match(e.Name)))
            .Where(c => c.Match.Success)
            .Select(c => (
                c.Entry,
                Tier: TierOf(c.Match.Groups["name"].Value),
                Density: DensityOf(c.Match.Groups["qualifier"].Value)))
            .Where(c => c.Tier >= 0)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(c => c.Tier)
            .ThenByDescending(c => c.Density)
            .ThenByDescending(c => c.Entry.Length)

            // Départage final pour que le choix ne dépende jamais de l'ordre
            // dans lequel l'archive a été lue.
            .ThenBy(c => c.Entry.Name, StringComparer.Ordinal)
            .First()
            .Entry.Name;
    }

    /// <summary>
    /// L'icône entière d'abord, sa variante ronde ensuite. Les morceaux d'une
    /// icône adaptative, « _foreground » et « _background », ne sont pas des
    /// icônes : montrer l'un des deux seul donnerait une image tronquée.
    /// </summary>
    private static int TierOf(string name) => name switch
    {
        "ic_launcher" => 0,
        "ic_launcher_round" => 1,
        _ => -1,
    };

    /// <summary>
    /// Densité annoncée par le qualificatif du dossier, en points par pouce.
    /// Ce qui n'en annonce aucune passe en dernier.
    /// </summary>
    private static int DensityOf(string qualifier)
    {
        foreach (var part in qualifier.Split('-'))
        {
            switch (part)
            {
                case "ldpi": return 120;
                case "mdpi": return 160;
                case "hdpi": return 240;
                case "xhdpi": return 320;
                case "xxhdpi": return 480;
                case "xxxhdpi": return 640;
                default: continue;
            }
        }

        return 0;
    }

    [GeneratedRegex(
        @"^res/(?:mipmap|drawable)(?<qualifier>[^/]*)/(?<name>[^/]+)\.png$",
        RegexOptions.IgnoreCase)]
    private static partial Regex IconPattern();
}

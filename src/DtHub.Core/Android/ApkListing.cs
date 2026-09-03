using System.Globalization;
using System.Text.RegularExpressions;

namespace DtHub.Core.Android;

/// <summary>Une entrée d'archive : son nom et sa taille décompressée.</summary>
public readonly record struct ApkEntry(string Name, long Length);

/// <summary>
/// Lecture des sorties de <c>pm path</c> et de <c>unzip -l</c> relevées sur le
/// téléphone. Fonctions pures, vérifiables sur des sorties enregistrées.
/// </summary>
public static partial class ApkListing
{
    /// <summary>
    /// Chemins d'APK rendus par <c>pm path</c>, l'archive de base d'abord.
    ///
    /// L'ordre compte : l'icône est presque toujours dans <c>base.apk</c>, mais
    /// une application découpée par densité peut la loger dans son morceau
    /// d'écran. L'appelant les essaie donc dans cet ordre et s'arrête au
    /// premier qui donne quelque chose.
    /// </summary>
    public static IReadOnlyList<string> ParsePaths(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        List<string> paths = [];

        foreach (var line in output.Split('\n'))
        {
            var text = line.Trim();

            if (!text.StartsWith("package:", StringComparison.Ordinal))
            {
                continue;
            }

            var path = text["package:".Length..].Trim();

            if (path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        return [.. paths.OrderBy(Rank).ThenBy(p => p, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Entrées d'une archive, telles que <c>unzip -l</c> les aligne.
    ///
    /// La lecture est structurelle et non positionnelle : quatre champs, dont
    /// le premier est un nombre et le dernier va jusqu'au bout de la ligne. Ce
    /// qui n'a pas cette forme est écarté sans qu'on ait à le nommer, à savoir
    /// la ligne « Archive: », l'en-tête, les filets de tirets et le total. Un
    /// analyseur qui suivrait la mise en page casserait au premier téléphone
    /// dont l'outil range ses colonnes autrement ou date en un autre format.
    /// </summary>
    public static IReadOnlyList<ApkEntry> ParseEntries(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        List<ApkEntry> entries = [];

        foreach (Match match in EntryPattern().Matches(output))
        {
            if (long.TryParse(
                    match.Groups["length"].Value,
                    CultureInfo.InvariantCulture,
                    out var length))
            {
                entries.Add(new ApkEntry(match.Groups["name"].Value, length));
            }
        }

        return entries;
    }

    /// <summary>L'archive de base avant ses morceaux, et le reste après.</summary>
    private static int Rank(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];

        if (string.Equals(name, "base.apk", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return name.StartsWith("split_config", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    [GeneratedRegex(
        @"^\s*(?<length>\d+)\s+\S+\s+\S+\s+(?<name>\S.*?)\s*$",
        RegexOptions.Multiline)]
    private static partial Regex EntryPattern();
}

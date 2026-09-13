using System.Text.RegularExpressions;

namespace DtHub.Core.Android;

/// <summary>
/// Chooses, within an archive's listing, the entry that carries the
/// launcher icon.
///
/// Neither the manifest nor <c>resources.arsc</c> is read: that
/// would mean decoding Android's resource format to retrieve a name
/// the convention already gives. Android's template names this
/// resource "ic_launcher", and that holds for the targeted game, as
/// measured. When the convention does not hold, nothing is returned,
/// which is exactly equivalent to the previous state.
/// </summary>
public static partial class LauncherIconChoice
{
    /// <summary>
    /// Transfer ceiling, deliberately far above what is needed:
    /// fifty one kilobytes are enough for the densest of the game's
    /// icons, as measured. The two hundred and fifty six here are not
    /// sized for that icon but against the unknown archive that would
    /// stash a one megabyte image under this name.
    /// </summary>
    public const long MaximumBytes = 256 * 1024;

    /// <summary>
    /// The entry to extract, or <c>null</c> when the archive has no
    /// raster icon. This is the case for an application that only
    /// ships an adaptive icon, described in XML and drawn by the
    /// launcher: rendering it would require a vector rasterizer and a
    /// resource reader.
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

            // Final tiebreaker so the choice never depends on the
            // order in which the archive was read.
            .ThenBy(c => c.Entry.Name, StringComparer.Ordinal)
            .First()
            .Entry.Name;
    }

    /// <summary>
    /// The full icon first, its round variant next. The pieces of an
    /// adaptive icon, "_foreground" and "_background", are not
    /// icons: showing just one of the two would give a cropped image.
    /// </summary>
    private static int TierOf(string name) => name switch
    {
        "ic_launcher" => 0,
        "ic_launcher_round" => 1,
        _ => -1,
    };

    /// <summary>
    /// Density advertised by the folder's qualifier, in dots per
    /// inch. Whatever advertises none comes last.
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

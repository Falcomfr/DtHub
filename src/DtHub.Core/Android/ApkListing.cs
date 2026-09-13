using System.Globalization;
using System.Text.RegularExpressions;

namespace DtHub.Core.Android;

/// <summary>An archive entry: its name and its uncompressed size.</summary>
public readonly record struct ApkEntry(string Name, long Length);

/// <summary>
/// Reading the output of <c>pm path</c> and <c>unzip -l</c>
/// collected from the phone. Pure functions, checkable against
/// recorded output.
/// </summary>
public static partial class ApkListing
{
    /// <summary>
    /// APK paths returned by <c>pm path</c>, the base archive first.
    ///
    /// Order matters: the icon is almost always in <c>base.apk</c>,
    /// but an application split by density may place it in its
    /// screen-density piece. The caller therefore tries them in
    /// this order and stops at the first one that yields something.
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
    /// Entries of an archive, as <c>unzip -l</c> lines them up.
    ///
    /// Reading is structural, not positional: four fields, of which
    /// the first is a number and the last runs to the end of the
    /// line. Anything that does not have this shape is discarded
    /// without needing to be named specifically, namely the
    /// "Archive:" line, the header, the dashed rules, and the
    /// total. A parser that followed the layout would break on the
    /// first phone whose tool arranges its columns differently or
    /// dates in another format.
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

    /// <summary>
    /// The base archive before its pieces, and the rest after.
    /// </summary>
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

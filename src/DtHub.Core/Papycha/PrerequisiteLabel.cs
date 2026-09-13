using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// What a prerequisite label really designates.
///
/// The site writes these in three forms, and two of them are not
/// quest titles. A milestone is not a quest but the state it leaves
/// behind: "L'essentiel est dans le Lac gelé atteint" (roughly "The
/// essentials are in the Frozen Lake, reached"). And a whole
/// achievement is quoted as "Succès Un nouveau départ réalisé"
/// ("Achievement A new start, completed"). Measured over the
/// catalog's 584 prerequisites: 511 bare titles, 35 milestones, 38
/// achievements. The old brackets, "[FIN] ..." (French for "end"),
/// have disappeared from the site but the rule stays, since the
/// embedded catalog may date from before.
///
/// Matching them as is therefore left **one prerequisite in eight**
/// without a follow-up: that is how "La légende du Chevalier de
/// l'Automne" ("The Legend of the Autumn Knight") had no next quest,
/// the only edge leading to the following achievement being carried
/// by an achievement label.
///
/// The rule is the same as the one in the extraction script,
/// <c>build/extract-successes.py</c>, function <c>sans_marque</c>.
/// Until now it only lived on the Python side, which did not copy
/// its result into the file: the application had no way to know it.
/// </summary>
public static partial class PrerequisiteLabel
{
    /// <summary>What a prerequisite label names.</summary>
    /// <param name="Name">The name, with markers removed.</param>
    /// <param name="IsSuccess">
    /// True when this is a whole achievement, not a quest.
    /// </param>
    public readonly record struct Target(string Name, bool IsSuccess);

    /// <summary>Reads a prerequisite label.</summary>
    public static Target Of(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return new Target(string.Empty, false);
        }

        var value = BracketPattern().Replace(label, string.Empty).Trim();

        if (SuccessPattern().Match(value) is { Success: true } success)
        {
            return new Target(success.Groups["name"].Value.Trim(), true);
        }

        return new Target(ReachedPattern().Replace(value, string.Empty).Trim(), false);
    }

    [GeneratedRegex(@"^\[[^\]]*\]\s*", RegexOptions.None, 250)]
    private static partial Regex BracketPattern();

    [GeneratedRegex(
        @"^Succ[èe]s\s+(?<name>.+?)\s+r[ée]alis[ée]e?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex SuccessPattern();

    [GeneratedRegex(
        @"\s+atteint(?:e)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex ReachedPattern();
}

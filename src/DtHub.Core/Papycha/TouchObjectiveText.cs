using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>What a hole of an objective template stands for: a plain number or a game name to look up.</summary>
public enum TouchHole { Count, Npc, Item, Monster, Area, Text }

/// <summary>
/// The game words an objective as a template ("Take #3 #2 to #1") whose holes
/// are filled from the objective's parameters.
/// </summary>
public static partial class TouchObjectiveText
{
    /// <param name="typeId">The objective type.</param>
    /// <param name="hole">1-based, like in the template.</param>
    public static TouchHole KindOf(int typeId, int hole) => (typeId, hole) switch
    {
        (1 or 2 or 3 or 9 or 12, 1) => TouchHole.Npc,
        (2 or 3, 2) or (20, 1) => TouchHole.Item,
        (6 or 12, 2) or (14 or 45 or 46, 1) => TouchHole.Monster,
        (5, 1) => TouchHole.Area,
        (0 or 4 or 10, 1) => TouchHole.Text,
        _ => TouchHole.Count,
    };

    /// <summary>A missing name (or parameter) leaves the number (or the hole) as it is.</summary>
    public static string Fill(
        string template, int typeId, IReadOnlyList<int> parameters, Func<TouchHole, int, string?> name) =>
        Holes().Replace(template, match =>
        {
            var hole = match.Groups[1].Value[0] - '0';

            if (hole > parameters.Count)
            {
                return match.Value;
            }

            var value = parameters[hole - 1];
            var kind = KindOf(typeId, hole);
            var named = kind == TouchHole.Count ? null : name(kind, value);

            return string.IsNullOrEmpty(named) ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : named;
        });

    [GeneratedRegex("#([1-3])")]
    private static partial Regex Holes();
}

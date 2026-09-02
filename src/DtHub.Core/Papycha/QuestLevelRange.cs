using System.Globalization;

namespace DtHub.Core.Papycha;

/// <summary>
/// La plage de niveaux d'un ensemble de quêtes, telle qu'on la montre, ou rien
/// quand ce qu'on en dirait serait faux.
///
/// Le site ne renseigne le niveau que sur cent dix-sept quêtes sur sept cent
/// quatre-vingt-deux, et sur plusieurs zones aucune. Une plage tirée d'une
/// seule quête sur vingt-trois passerait pour la plage de la zone.
/// </summary>
public static class QuestLevelRange
{
    /// <summary>
    /// La plage, ou <c>null</c> quand trop peu de quêtes portent un niveau.
    ///
    /// Le seuil est de trois quêtes renseignées, sauf si elles le sont toutes :
    /// une zone de deux quêtes qui portent toutes deux leur niveau dit une
    /// plage juste. Le nombre de quêtes sur lequel la plage repose est rappelé
    /// dès qu'il est partiel, pour qu'on sache ce qu'on lit.
    /// </summary>
    public static string? Of(IReadOnlyList<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        List<int> levels = [.. quests.Where(q => q.Level > 0).Select(q => q.Level)];

        if (levels.Count == 0 || (levels.Count < 3 && levels.Count < quests.Count))
        {
            return null;
        }

        var low = levels.Min();
        var high = levels.Max();
        var span = low == high ? Text(low) : $"{Text(low)} - {Text(high)}";

        return levels.Count == quests.Count
            ? $"niveau {span}"
            : $"niveau {span} (sur {Text(levels.Count)})";
    }

    private static string Text(int value) => value.ToString(CultureInfo.CurrentCulture);
}

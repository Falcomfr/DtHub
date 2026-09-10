namespace DtHub.Core.Almanax;

/// <summary>
/// La grille d'un mois, telle qu'un calendrier la montre.
///
/// Six semaines et non cinq : un mois de trente et un jours qui commence le
/// dernier jour de la semaine s'étale sur six lignes, et une grille de taille
/// variable ferait sauter la hauteur de la fenêtre d'un mois à l'autre. Six
/// lignes couvrent tous les cas et n'en changent aucun.
///
/// Le premier jour de la semaine vient de la culture : lundi en France,
/// dimanche aux États-Unis. Le supposer aurait décalé la grille d'un cran pour
/// une moitié du monde, et un décalage d'un cran ne se voit pas, il se subit.
/// </summary>
public static class MonthGrid
{
    /// <summary>Lignes de la grille.</summary>
    public const int Weeks = 6;

    /// <summary>Colonnes de la grille, soit les jours d'une semaine.</summary>
    public const int Span = 7;

    /// <summary>
    /// Les quarante-deux jours de la grille, débords compris : la fin du mois
    /// précédent et le début du suivant remplissent les cases des bords.
    /// </summary>
    public static IReadOnlyList<DateOnly> For(int year, int month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateOnly(year, month, 1);
        var shift = ((int)first.DayOfWeek - (int)firstDayOfWeek + Span) % Span;
        var start = first.AddDays(-shift);

        var days = new DateOnly[Weeks * Span];

        for (var i = 0; i < days.Length; i++)
        {
            days[i] = start.AddDays(i);
        }

        return days;
    }

    /// <summary>
    /// Les jours de la semaine, du premier au dernier selon la culture, pour
    /// coiffer les colonnes.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> Header(DayOfWeek firstDayOfWeek)
    {
        var days = new DayOfWeek[Span];

        for (var i = 0; i < Span; i++)
        {
            days[i] = (DayOfWeek)(((int)firstDayOfWeek + i) % Span);
        }

        return days;
    }
}

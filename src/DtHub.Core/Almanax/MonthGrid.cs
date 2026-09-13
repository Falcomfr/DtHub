namespace DtHub.Core.Almanax;

/// <summary>
/// The grid of a month, as a calendar shows it.
///
/// Six weeks and not five: a thirty-one-day month that starts on
/// the last day of the week spreads across six rows, and a grid of
/// variable size would make the window's height jump from one month
/// to the next. Six rows cover every case and change none of them.
///
/// The first day of the week comes from culture: Monday in France,
/// Sunday in the United States. Assuming one would have shifted the
/// grid by one slot for half the world, and a one-slot shift does
/// not show, it is simply endured.
/// </summary>
public static class MonthGrid
{
    /// <summary>Rows of the grid.</summary>
    public const int Weeks = 6;

    /// <summary>Columns of the grid, i.e. the days of a week.</summary>
    public const int Span = 7;

    /// <summary>
    /// The forty-two days of the grid, overflow included: the end of
    /// the previous month and the start of the next one fill the
    /// edge cells.
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
    /// The days of the week, from first to last according to
    /// culture, to head the columns.
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

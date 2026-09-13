namespace DtHub.Core.Almanax;

/// <summary>
/// How far back and forward the Almanax can be browsed.
///
/// The calendar has no end in itself: it repeats every year on the
/// Gregorian date, and the portal answers for any date at all.
/// Measured, it returns the same day's entry in 2026, 2050 and
/// 2100, and it correctly recalculates the seven mérydes (the
/// calendar's own weekdays) with movable dates: Etnop falls on
/// April 5, 2026, March 28, 2027 and April 21, 2030, which are the
/// true Easter Sundays.
///
/// **Yet two things force a bound.**
///
/// The first is a lie the portal tells. Asked about a date outside
/// what it can handle, "0001-01-01" or a month numbered thirteen,
/// it does not complain: it returns **today's entry**. Our banner
/// would then announce one date while showing the offering of
/// another, with nothing to say so.
///
/// The second is <c>DateOnly</c> itself, which runs from year 1 to
/// December 31, 9999: stepping back one day from its earliest date
/// throws, and the window would die on one click of the arrow.
///
/// Five years on each side, then. That is far beyond any real use,
/// since the calendar says nothing new past a year except for the
/// movable feasts, and it stays well clear of both traps.
/// </summary>
public static class AlmanaxRange
{
    /// <summary>Years browsable on each side of today.</summary>
    public const int Years = 5;

    /// <summary>The earliest browsable date.</summary>
    public static DateOnly Earliest(DateOnly today) => today.AddYears(-Years);

    /// <summary>The furthest browsable date.</summary>
    public static DateOnly Latest(DateOnly today) => today.AddYears(Years);

    /// <summary>True if the date can be browsed.</summary>
    public static bool Contains(DateOnly date, DateOnly today) =>
        date >= Earliest(today) && date <= Latest(today);

    /// <summary>
    /// The date brought back within bounds.
    ///
    /// Clamping rather than refusing: an arrow pressed once too
    /// many times should stop at the edge, not do nothing
    /// inexplicable.
    /// </summary>
    public static DateOnly Clamp(DateOnly date, DateOnly today)
    {
        var first = Earliest(today);
        var last = Latest(today);

        return date < first ? first : date > last ? last : date;
    }

    /// <summary>
    /// The month shift applied without ever going out of bounds.
    ///
    /// The whole month moves at once, and the first day of the
    /// resulting month can fall before the bound even though the
    /// month itself still contains it: we therefore compare on the
    /// month, not on the day.
    /// </summary>
    public static DateOnly ShiftMonth(DateOnly month, int months, DateOnly today)
    {
        var first = Earliest(today);
        var last = Latest(today);

        // The shift is counted in whole months rather than added to
        // the date: "AddMonths" throws on its own before we can
        // clamp it, and a named test is what showed me this.
        var wanted = Index(month.Year, month.Month) + months;

        wanted = Math.Clamp(wanted, Index(first.Year, first.Month), Index(last.Year, last.Month));

        return new DateOnly((int)(wanted / 12), (int)(wanted % 12) + 1, 1);
    }

    /// <summary>
    /// The rank of a month since year zero, to count without
    /// overflow.
    /// </summary>
    private static long Index(int year, int month) => ((long)year * 12) + month - 1;
}

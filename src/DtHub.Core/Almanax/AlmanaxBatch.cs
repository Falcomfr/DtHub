namespace DtHub.Core.Almanax;

/// <summary>
/// Reading several days in a row, on request.
///
/// The portal has no page that gives several offerings at once: the
/// week page carries the bonus titles only, and only for the seven
/// days after today. Preparing the week's offerings, or finding the
/// next day of a given bonus, therefore reads the days one by one.
/// The reader is handed in: it is the window's, which loads the page,
/// paces the reads and keeps what it read for the session. Here is
/// only the order and the stop.
/// </summary>
public static class AlmanaxBatch
{
    /// <summary>
    /// How far a search looks ahead. Most bonuses come back within a
    /// month or so, the gathering ones about every five weeks over
    /// 2027: two months find nearly all of them, at one page a
    /// second. The once-a-year ones are left to the calendar.
    /// </summary>
    public const int SearchDays = 60;

    /// <summary>
    /// Each day in order, with its reading, or <c>null</c> when it
    /// could not be read: the line stays, so a missing day does not
    /// pass for a day with nothing to bring.
    /// </summary>
    public static async Task<IReadOnlyList<(DateOnly Date, AlmanaxDay? Day)>> ReadAsync(
        IEnumerable<DateOnly> dates,
        Func<DateOnly, CancellationToken, Task<AlmanaxDay?>> read,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(read);

        var days = new List<(DateOnly, AlmanaxDay?)>();

        foreach (var date in dates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Back on the caller's context, not the pool: the reader is
            // the window's, and its WebView2 only answers on the
            // interface thread. Measured: from the second day on, it
            // threw.
            days.Add((date, await read(date, cancellationToken).ConfigureAwait(true)));

            progress?.Report(days.Count);
        }

        return days;
    }

    /// <summary>
    /// The first day from <paramref name="from" /> on whose bonus
    /// title mentions <paramref name="query" />, or <c>null</c> within
    /// <paramref name="days" /> days. A day that cannot be read is
    /// passed over.
    /// </summary>
    public static async Task<AlmanaxDay?> FindAsync(
        DateOnly from,
        int days,
        string query,
        Func<DateOnly, CancellationToken, Task<AlmanaxDay?>> read,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);

        for (var i = 0; i < days; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // On the caller's context, for the same reason as above.
            var day = await read(from.AddDays(i), cancellationToken).ConfigureAwait(true);

            progress?.Report(i + 1);

            if (day is not null && AlmanaxReading.Mentions(day.Bonus, query))
            {
                return day;
            }
        }

        return null;
    }
}

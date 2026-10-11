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
    /// Every day from <paramref name="from" /> on, within
    /// <paramref name="days" /> days, that <paramref name="match" />
    /// accepts, each one also handed to <paramref name="found" /> as
    /// soon as it is read: a search of two months takes a minute, and
    /// the list fills in meanwhile. A day that cannot be read is
    /// passed over.
    /// </summary>
    public static async Task<IReadOnlyList<AlmanaxDay>> FindAllAsync(
        DateOnly from,
        int days,
        Func<AlmanaxDay, bool> match,
        Func<DateOnly, CancellationToken, Task<AlmanaxDay?>> read,
        IProgress<int>? progress,
        Action<AlmanaxDay>? found,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(read);

        var matches = new List<AlmanaxDay>();

        for (var i = 0; i < days; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // On the caller's context, for the same reason as above.
            var day = await read(from.AddDays(i), cancellationToken).ConfigureAwait(true);

            progress?.Report(i + 1);

            if (day is not null && match(day))
            {
                matches.Add(day);
                found?.Invoke(day);
            }
        }

        return matches;
    }
}

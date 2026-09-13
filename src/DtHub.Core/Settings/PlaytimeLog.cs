using System.Globalization;

namespace DtHub.Core.Settings;

/// <summary>
/// Time spent on an account, day by day, over a rolling week.
///
/// Information only: no limit, no reminder, no judgment. Someone
/// playing five accounts ends up no longer knowing which one they
/// are really running, and that is the only question this log
/// answers.
///
/// **Seven days, no more.** A running total since forever says
/// nothing useful and grows forever in the settings file; a week
/// fits in seven entries and is enough to see where the time goes.
///
/// The key is the date in ISO format, "2026-09-10". It is the only
/// format that sorts the way it reads and does not depend on any
/// culture.
/// </summary>
public static class PlaytimeLog
{
    /// <summary>Days kept.</summary>
    public const int Days = 7;

    /// <summary>The key for a day.</summary>
    public static string KeyOf(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Adds a duration to the given day and discards whatever falls
    /// outside the week.
    ///
    /// Returns a new log rather than modifying the one received: the
    /// caller writes into a settings document, and an in-place
    /// modification would go unnoticed there.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Add(
        IReadOnlyDictionary<string, int>? existing,
        DateOnly day,
        int seconds)
    {
        Dictionary<string, int> log = existing is null
            ? new(StringComparer.Ordinal)
            : new(existing, StringComparer.Ordinal);

        if (seconds > 0)
        {
            var key = KeyOf(day);

            log[key] = (log.TryGetValue(key, out var already) ? already : 0) + seconds;
        }

        return Trim(log, day);
    }

    /// <summary>
    /// Discards days earlier than the week ending on the given day,
    /// and those whose key is not a date.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Trim(
        IReadOnlyDictionary<string, int>? log,
        DateOnly today)
    {
        if (log is null)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var first = today.AddDays(-(Days - 1));

        Dictionary<string, int> kept = new(StringComparer.Ordinal);

        foreach (var (key, seconds) in log)
        {
            // An unreadable key is dropped: this is a file someone
            // may have edited by hand, and a log does not need to
            // survive its own corruption.
            if (seconds > 0 && Parse(key) is { } day && day >= first && day <= today)
            {
                kept[key] = seconds;
            }
        }

        return kept;
    }

    /// <summary>
    /// The total for the week ending on the given day, in seconds.
    /// </summary>
    public static int Week(IReadOnlyDictionary<string, int>? log, DateOnly today) =>
        Trim(log, today).Values.Sum();

    private static DateOnly? Parse(string key) =>
        DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
}

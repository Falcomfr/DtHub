using DtHub.Core.Localization;

namespace DtHub.Core.Sessions;

/// <summary>
/// How long a window has been open, said in words.
///
/// **Minutes, never seconds.** The panel refreshes on the device
/// sweep, every two to six seconds depending on the tier, which is ten
/// times finer than a minute and coarse enough that nothing here ever
/// needs a clock of its own. A second-by-second counter would also
/// change what the row is: "1 h 12" is a state, "1:12:04" is a
/// stopwatch, and this application counts nothing for anyone.
///
/// The playtime of the week is said elsewhere and in its own words,
/// because <c>PlaytimeHours</c> carries "this week" inside its value.
/// A session is the other question, so it has its own keys.
/// </summary>
public static class SessionClock
{
    /// <summary>
    /// Below this, there is no figure worth printing, and the label
    /// says the window has only just opened.
    /// </summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the window has been open.
    ///
    /// Never <c>null</c> and never empty: the label appears with the
    /// session and stays. Returning nothing under a minute would make
    /// the row grow a line sixty seconds after it opened, which is
    /// exactly the jump the always-present status line exists to
    /// avoid.
    ///
    /// A negative span is possible, and it is not worth a special
    /// case: a clock stepped back by an hour, or a session whose start
    /// was read after the machine resynchronised, both land here. They
    /// read as freshly opened, which is wrong by an hour and harmless,
    /// where a negative figure would be alarming and just as wrong.
    /// </summary>
    public static string Describe(TimeSpan elapsed) => elapsed < Fresh
        ? Strings.Get("SessionJustOpened")
        : elapsed.TotalHours >= 1
            ? Strings.Format("SessionHours", (int)elapsed.TotalHours, elapsed.Minutes)
            : Strings.Format("SessionMinutes", elapsed.Minutes);
}

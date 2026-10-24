namespace DtHub.Core.Papycha;

/// <summary>
/// What "Recommandées" says in place of its rows while they cannot show: a string key, empty when they show. The waits
/// are long and normal: the site may take its thirty seconds, the game's first read some minutes.
/// </summary>
public static class ProfitWait
{
    public const string ReadingTracker = "ProfitReadingTracker";
    public const string ReadingGame = "ProfitReadingGame";
    public const string TrackerDown = "ProfitTrackerDown";
    public const string GameDown = "ProfitGameDown";
    public const string NeedsLink = "ProfitNeedsLink";

    /// <param name="link">Where the tracker link stands.</param>
    /// <param name="reading">A tracker read is under way.</param>
    /// <param name="facts">The game's quest facts read so far, null before any.</param>
    /// <param name="factsDone">The facts' read has ended: none then means the data is missing, whatever the cause (a
    /// later read of a guide's steps clears the service's last failure).</param>
    public static string Of(TrackerLink link, bool reading, int? facts, bool factsDone) =>
        link == TrackerLink.Linked
            ? facts is null || (facts == 0 && !factsDone) ? ReadingGame
                : facts == 0 ? GameDown
                : string.Empty
            : reading || link == TrackerLink.Unknown ? ReadingTracker
            : link == TrackerLink.Unreadable ? TrackerDown
            : NeedsLink;

    /// <summary>A wait the spinner turns for.</summary>
    public static bool Spins(string wait) => wait is ReadingTracker or ReadingGame;

    /// <summary>Only the tracker is read again on demand: the game's server keeps its own back-off.</summary>
    public static bool CanRetry(string wait) => wait == TrackerDown;
}

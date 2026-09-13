namespace DtHub.Core.Devices;

/// <summary>
/// One row of the error banner, with what it is worth.
///
/// The severity travels with the line and not beside it: the banner
/// used to take its colour from the worst finding of the whole
/// application, including the ones it does not show, so a phone whose
/// row carried its own serious finding reddened a banner that was
/// talking about something else.
/// </summary>
public readonly record struct BannerLine(string Text, HealthSeverity Severity)
{
    /// <summary>A line that is worth knowing without being grave.</summary>
    public static BannerLine Of(string text) => new(text, HealthSeverity.Warning);

    /// <summary>A line that will end the session if nothing is done.</summary>
    public static BannerLine Grave(string text) => new(text, HealthSeverity.Serious);
}

/// <summary>What the banner shows, once everything has been said.</summary>
/// <param name="Text">
/// The rows, one per line, or <c>null</c> when there is nothing to
/// say. The banner is hidden on <c>null</c> and shown otherwise: that
/// single answer is the point of this type.
/// </param>
/// <param name="IsSerious">
/// True when one of the rows <em>shown</em> will end a session.
/// </param>
public readonly record struct BannerContent(string? Text, bool IsSerious)
{
    /// <summary>Nothing to show.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Text);
}

/// <summary>
/// What the error banner of the account list shows, decided in one
/// place.
///
/// **It used to be decided in three, and they did not agree.** The
/// banner took its visibility from one property, its text from a
/// second and its colour from a third, each written by a different
/// path. A launch failure wrote only the first: the banner appeared,
/// and displayed the health notice left over from the previous sweep.
/// The text of the failure was reachable by no path at all.
///
/// The rule is here rather than in the view model because that is the
/// only layer the tests reach: <c>tests/DtHub.Tests</c> does not
/// reference <c>DtHub.App</c>, so a rule left in the window is a rule
/// nothing proves. Same reason as <see cref="DeviceHealth" /> next
/// door, which this one leans on for its severities.
///
/// **One line per finding, and not the worst of them.** That was paid
/// for once: a phone carried three at the same time, lock, low
/// storage and unprepared battery, and showing only the gravest hid
/// the other two so thoroughly that the first had to be fixed to
/// learn the second existed.
/// </summary>
public static class ErrorBanner
{
    /// <summary>
    /// The banner for these rows, blank ones set aside.
    ///
    /// A blank row is not an absence of problem, it is a message
    /// somebody forgot to write; letting it through would show an
    /// empty banner, which reads as a fault in the application
    /// itself.
    /// </summary>
    public static BannerContent Of(IEnumerable<BannerLine>? lines)
    {
        List<BannerLine> kept = [.. (lines ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Text))];

        if (kept.Count == 0)
        {
            return default;
        }

        return new BannerContent(
            string.Join(Environment.NewLine, kept.Select(l => l.Text)),
            kept.Exists(l => l.Severity == HealthSeverity.Serious));
    }
}

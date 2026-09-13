namespace DtHub.Core.Devices;

/// <summary>
/// A notice that stops being true after a while.
///
/// **The expiry is read, not swept.** The three notices of the
/// account panel used to be cleared by the periodic sweep, which is
/// the one thing that stops when the panel is hidden: a notice raised
/// just before hiding it was still there, word for word, when the
/// panel came back hours later. Asking the notice itself whether it
/// is still true costs a subtraction and depends on no timer being
/// alive.
///
/// It carries its moment rather than a countdown, so that reading it
/// twice gives the same answer and reading it never does not keep it
/// alive.
/// </summary>
/// <param name="Text">
/// What the notice says. Blank means there is no notice, which is
/// what <c>default</c> gives.
/// </param>
/// <param name="Since">When it was raised.</param>
/// <param name="Severity">What it is worth in a banner.</param>
public readonly record struct TimedNotice(string? Text, DateTimeOffset Since, HealthSeverity Severity)
{
    /// <summary>A notice raised now, worth knowing without being grave.</summary>
    public static TimedNotice Raised(string? text, DateTimeOffset now) =>
        new(text, now, HealthSeverity.Warning);

    /// <summary>True when something was actually said.</summary>
    public bool Exists => !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// True while the notice still describes something that is
    /// happening.
    /// </summary>
    public bool IsLiveAt(DateTimeOffset now, TimeSpan life) => Exists && now - Since < life;

    /// <summary>
    /// The banner line this notice deserves, or <c>null</c> when its
    /// time is up.
    /// </summary>
    public BannerLine? LineAt(DateTimeOffset now, TimeSpan life) =>
        IsLiveAt(now, life) ? new BannerLine(Text!, Severity) : null;
}

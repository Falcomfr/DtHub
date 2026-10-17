namespace DtHub.Core.Devices;

/// <summary>
/// A remembered phone gone long enough to stop asking for it at every pass.
///
/// A Mi 9T Pro last seen on 2026-09-20 was still asked to reconnect every
/// five seconds on 2026-10-17: an adb connect with its five-second deadline,
/// a network scan forced because one known phone was missing, and the panel's
/// sweep held up behind them. A dormant phone is still found by its own
/// announcement on the network, and asked at its last address now and then.
/// </summary>
public static class DeviceDormancy
{
    /// <summary>How long a phone may be away before it sleeps.</summary>
    public static readonly TimeSpan After = TimeSpan.FromDays(1);

    /// <summary>How often a dormant phone is still asked at its last address.</summary>
    public static readonly TimeSpan RetryEvery = TimeSpan.FromMinutes(5);

    /// <summary>
    /// True for a phone last seen longer than <see cref="After" /> ago. Never
    /// seen means just paired, to be tried at once.
    /// </summary>
    public static bool IsDormant(DateTimeOffset? lastSeenUtc, DateTimeOffset nowUtc) =>
        lastSeenUtc is { } seen && nowUtc - seen > After;
}

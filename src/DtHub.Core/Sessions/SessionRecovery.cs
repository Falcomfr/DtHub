using DtHub.Core.Dofus;
using DtHub.Core.Scrcpy;

namespace DtHub.Core.Sessions;

/// <summary>What we know about a session that has just gone dark.</summary>
/// <param name="Requested">True if it was us who asked for the stop.</param>
/// <param name="EverRan">
/// True if the session had truly opened its display and launched the
/// game. An opening failure is already handled during launch, by
/// falling back to a more modest resolution; it has no business here.
/// </param>
/// <param name="Failure">
/// The nature of the refusal, as scrcpy's output stated it.
/// </param>
public readonly record struct SessionEnd(bool Requested, bool EverRan, ScrcpyFailureKind Failure);

/// <summary>A window to reopen, and after how long.</summary>
public sealed record RecoveryRequest(DofusInstance Instance, TimeSpan Delay);

/// <summary>What to do next about a session that has gone dark.</summary>
public readonly record struct RecoveryDecision(bool Retry, TimeSpan Delay)
{
    /// <summary>We do nothing.</summary>
    public static readonly RecoveryDecision None = new(false, TimeSpan.Zero);
}

/// <summary>
/// Should a game window that went dark on its own be reopened.
///
/// The question arises because the application did not use to survive
/// the answer: when the last session died without us having asked for
/// it, DT Hub closed. Yet disconnections are the number one complaint
/// of DOFUS Touch players, and a Wi-Fi link that hiccups is no reason
/// to lose one's game session and one's application along with it.
///
/// **The distinction that matters is between an intended end and a
/// failure.** A window closed by hand must stay closed: reopening it
/// would be maddening. Yet from our point of view, the two look
/// alike, no code of ours having been called in either case. What
/// separates them is that scrcpy exits cleanly when its window is
/// closed, and with an error when the link drops. **We therefore only
/// reopen on a declared refusal**, and never on a clean exit.
///
/// The rest of the rules rule out what insisting would not cure, in
/// the same spirit as
/// <see cref="ScrcpyOutputParser.CanRetrySmaller" />: an unauthorized
/// device will stay that way, and scrcpy missing from the machine
/// will not install itself.
/// </summary>
public static class SessionRecovery
{
    /// <summary>
    /// Number of attempts, beyond which we stop and say so.
    ///
    /// Three, because a passing hiccup rarely lasts more than twenty
    /// seconds, and a lasting failure is not solved by insisting. An
    /// application that reopens a window that keeps falling back
    /// indefinitely is worse than an application that stops: it takes
    /// up space without being of use.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// After this much time with no new failure, the attempt count
    /// starts over from zero. Otherwise a six-hour session would end
    /// up exhausting its credit on incidents unrelated to each other.
    /// </summary>
    public static readonly TimeSpan Forget = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The wait before attempt number <paramref name="attempt" />,
    /// counted starting from one.
    ///
    /// Increasing: the first failure deserves an immediate retry, the
    /// third deserves letting the Wi-Fi recover.
    /// </summary>
    public static TimeSpan DelayFor(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(15),
    };

    /// <summary>
    /// True if the refusal has a chance of not happening again.
    ///
    /// <see cref="ScrcpyFailureKind.None" /> is excluded from it, and
    /// this is the important point: no declared refusal means a clean
    /// exit, hence a window closed by hand.
    /// </summary>
    public static bool Recoverable(ScrcpyFailureKind kind) => kind
        is ScrcpyFailureKind.DeviceDisconnected
        or ScrcpyFailureKind.DeviceGone
        or ScrcpyFailureKind.ConnectionFailed
        or ScrcpyFailureKind.Unknown;

    /// <summary>
    /// What to do next.
    /// </summary>
    /// <param name="end">What we know about the end.</param>
    /// <param name="attemptsAlready">
    /// Attempts already made for this instance within the current
    /// time window, zero at the first failure.
    /// </param>
    public static RecoveryDecision Decide(SessionEnd end, int attemptsAlready)
    {
        if (end.Requested || !end.EverRan || !Recoverable(end.Failure) || attemptsAlready >= MaxAttempts)
        {
            return RecoveryDecision.None;
        }

        return new RecoveryDecision(true, DelayFor(attemptsAlready + 1));
    }
}

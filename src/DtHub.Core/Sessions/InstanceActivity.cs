using DtHub.Core.Scrcpy;

namespace DtHub.Core.Sessions;

/// <summary>
/// What an account is doing, as a row has to show it.
///
/// **A window being built and a window showing the game were the same
/// thing.** The screen asked one question, "is it open", and got one
/// boolean: a row went from the play button to the stop button the
/// instant scrcpy was asked for a display, seconds before anything
/// appeared, and said nothing at all while a dropped link was being
/// brought back. Four states, because those are the four a reader can
/// act on differently.
/// </summary>
public enum InstanceActivity
{
    /// <summary>No window, and none on the way.</summary>
    Idle,

    /// <summary>A window is being built. Nothing to see yet.</summary>
    Starting,

    /// <summary>The window is there and shows the game.</summary>
    Running,

    /// <summary>
    /// The link dropped and the window is being brought back. There is
    /// nothing to do but wait, which is exactly why it has to be said.
    /// </summary>
    Recovering,
}

/// <summary>
/// How an activity is worked out, and what it means.
///
/// The rules live here rather than in the view model so that they can
/// be tested: three bindings in the account row hang off
/// <see cref="HasWindow" />, and a mistake in it swaps the play button
/// for the stop button.
/// </summary>
public static class InstanceActivities
{
    /// <summary>
    /// What the account is doing, from what scrcpy says about its
    /// session and whether a recovery is under way.
    /// </summary>
    /// <param name="session">
    /// The state of its session, or <c>null</c> when it has none. Only
    /// living sessions are ever offered here, so <c>Failed</c> and
    /// <c>Stopped</c> arrive as <c>null</c>.
    /// </param>
    /// <param name="recovering">
    /// True while its window is being brought back. Only consulted
    /// when there is no session, since a session that exists already
    /// says more.
    /// </param>
    public static InstanceActivity Of(ScrcpySessionState? session, bool recovering) => session switch
    {
        ScrcpySessionState.Starting => InstanceActivity.Starting,
        ScrcpySessionState.Running => InstanceActivity.Running,
        _ => recovering ? InstanceActivity.Recovering : InstanceActivity.Idle,
    };

    /// <summary>
    /// True when a window exists for this account, which is what
    /// decides whether the row offers to open it or to close it.
    ///
    /// **Recovery is deliberately not a window.** The window is gone;
    /// what exists is a promise to reopen it. Counting it here would
    /// show a stop button for something there is nothing to stop.
    /// </summary>
    public static bool HasWindow(this InstanceActivity activity) =>
        activity is InstanceActivity.Starting or InstanceActivity.Running;
}

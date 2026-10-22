namespace DtHub.Core.Settings;

/// <summary>
/// The open windows still running with a startup setting changed since
/// they opened (D216).
///
/// Changing such a setting used to reopen every window by itself, and a
/// reopen restarts the game on the phone, which logs the accounts out.
/// The windows now wait for the player's "Reopen now"; a window opened
/// after the change already reads the new setting.
///
/// Sessions are held by reference: a window reopened by hand is a new
/// session, so it no longer waits. Meant for the interface's thread.
/// </summary>
public sealed class PendingReopen
{
    private readonly HashSet<object> _waiting = new(ReferenceEqualityComparer.Instance);

    /// <summary>True while a window open at a change is still open.</summary>
    public bool IsPending => _waiting.Count > 0;

    /// <summary>
    /// A startup setting changed. Returns true when a reopen is under way:
    /// it may have read the settings before the change, so it should run
    /// once more, as it did before D216.
    /// </summary>
    public bool SettingChanged(IEnumerable<object> openSessions, bool reopenRunning)
    {
        ArgumentNullException.ThrowIfNull(openSessions);

        if (reopenRunning)
        {
            return true;
        }

        _waiting.UnionWith(openSessions);
        return false;
    }

    /// <summary>The sessions moved: those closed or replaced no longer wait.</summary>
    public void SessionsChanged(IEnumerable<object> openSessions)
    {
        ArgumentNullException.ThrowIfNull(openSessions);

        _waiting.IntersectWith(openSessions);
    }

    /// <summary>The player asked for the reopen, which reads the current settings.</summary>
    public void Reopening() => _waiting.Clear();
}

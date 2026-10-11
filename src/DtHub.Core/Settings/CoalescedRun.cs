namespace DtHub.Core.Settings;

/// <summary>
/// A piece of work run once at a time, and once more after it when it
/// was asked for again meanwhile, however many times.
///
/// Written for the windows' reopening after a startup setting changes.
/// A second change during a reopen was dropped: the reopen running read
/// the settings before it, and nothing came after. The setting is now
/// always saved, and the reopen done again once at the end.
///
/// Meant for one thread, the interface's: no lock, and the awaits come
/// back on the caller's context.
/// </summary>
public sealed class CoalescedRun
{
    private bool _again;

    /// <summary>True while the work runs, its reruns included.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Runs the work, or, when it is already running, asks for one more
    /// pass and returns at once.
    /// </summary>
    public async Task RunAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (IsRunning)
        {
            _again = true;
            return;
        }

        IsRunning = true;

        try
        {
            do
            {
                _again = false;

                await work().ConfigureAwait(true);
            }
            while (_again);
        }
        finally
        {
            IsRunning = false;
        }
    }
}

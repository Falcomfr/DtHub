using DtHub.Core.Processes;
using DtHub.Core.Sessions;

namespace DtHub.Core.Scrcpy;

/// <summary>Life cycle of a mirroring session.</summary>
public enum ScrcpySessionState
{
    /// <summary>
    /// scrcpy is starting, the display has not been created yet.
    /// </summary>
    Starting,

    /// <summary>Session open, application launched.</summary>
    Running,

    /// <summary>
    /// The session could not open, or was interrupted by an error.
    /// </summary>
    Failed,

    /// <summary>Session closed normally.</summary>
    Stopped,
}

/// <summary>
/// A mirroring window opened by DT Hub. The object lives as long as
/// the corresponding scrcpy process, and carries what is needed to
/// find it again, move it and close it.
/// </summary>
public sealed class ScrcpySession
{
    internal ScrcpySession(string id, LaunchTarget target, string windowTitle, IProcessSession process)
    {
        Id = id;
        Target = target;
        WindowTitle = windowTitle;
        Process = process;
        StartedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Identity of the session, repeated in the window's title.
    /// </summary>
    public string Id { get; }

    public LaunchTarget Target { get; }

    /// <summary>ADB serial number used at launch.</summary>
    public string Serial => Target.Serial;

    /// <summary>
    /// Exact title of the scrcpy window. It is by this that the window
    /// manager finds the window to move.
    /// </summary>
    public string WindowTitle { get; }

    public int ProcessId => Process.ProcessId;

    /// <summary>
    /// Virtual display created by scrcpy, when there is one.
    /// </summary>
    public int? VirtualDisplayId { get; internal set; }

    /// <summary>
    /// Width to height ratio of the source, taken from the session's
    /// settings. The window manager uses it so as not to distort the
    /// picture: a phone is in portrait, a tablet in landscape.
    /// </summary>
    public double SourceAspectRatio { get; internal set; }

    /// <summary>
    /// Corresponding scrcpy window, once found. Is zero as long as the
    /// window has not appeared.
    /// </summary>
    public nint WindowHandle { get; internal set; }

    public ScrcpySessionState State { get; internal set; } = ScrcpySessionState.Starting;

    /// <summary>Displayable message explaining the failure, if any.</summary>
    public string? FailureMessage { get; internal set; }

    /// <summary>
    /// Nature of the refusal. Used to decide whether a second attempt
    /// makes sense, a question the displayed message does not answer.
    /// </summary>
    public ScrcpyFailureKind FailureKind { get; internal set; } = ScrcpyFailureKind.None;

    public DateTimeOffset StartedUtc { get; }

    /// <summary>
    /// True when the end was requested by the application, and not
    /// suffered.
    ///
    /// Without this marker, nothing distinguished a deliberate closing
    /// from a failure: both end up in the same state. See
    /// <see cref="SessionRecovery" />.
    /// </summary>
    public bool StopRequested { get; internal set; }

    /// <summary>
    /// True as soon as the session has been opened at least once.
    ///
    /// An opening failure is already handled during launch, by falling
    /// back to a more modest resolution. Resuming it afterward would
    /// double the attempts without adding anything.
    /// </summary>
    public bool EverRan { get; internal set; }

    /// <summary>
    /// What is known about the end, to decide whether to reopen.
    /// </summary>
    public SessionEnd End => new(StopRequested, EverRan, FailureKind);

    /// <summary>
    /// Time taken by the phone to open the virtual display, in
    /// milliseconds. This is the only part of startup that must be
    /// serialized: measuring it says how much the wait truly costs.
    /// </summary>
    public long DisplayReadyMs { get; set; }

    /// <summary>
    /// Total startup time, display and game opening included.
    /// </summary>
    public long StartupMs { get; set; }

    /// <summary>Name displayed in the session list.</summary>
    public string DisplayName => Target.DisplayName;

    public bool IsAlive => State is ScrcpySessionState.Starting or ScrcpySessionState.Running;

    /// <summary>
    /// Last lines written by scrcpy. Kept so that the caller can log
    /// them in case of failure: without them, a refusal from scrcpy
    /// boils down to "the session could not open".
    /// </summary>
    public IReadOnlyList<string> RecentOutput
    {
        get
        {
            lock (_output)
            {
                return [.. _output];
            }
        }
    }

    /// <summary>Command line used, for diagnostics.</summary>
    public string CommandLine { get; internal set; } = string.Empty;

    /// <summary>
    /// True if it is us who opened the game on the phone.
    ///
    /// What we did not open, we do not close. Without this restriction,
    /// a session that fails before launching anything would still stop
    /// the game, which could very well be running because someone was
    /// playing it on the phone.
    /// </summary>
    internal bool AppLaunchedByUs { get; set; }

    /// <summary>
    /// Zero as long as nobody has requested stopping the game on the
    /// phone.
    ///
    /// Two paths lead to this request, and they can cross: the
    /// voluntary closing, which waits for it to guarantee its execution
    /// before the application stops, and the end of output reading,
    /// which covers the window closed by hand. Both occur for the same
    /// session as soon as the closing is voluntary.
    /// </summary>
    private int _appStopClaimed;

    /// <summary>
    /// Claims the right to stop the game, and grants it only once.
    ///
    /// Stopping twice would have no consequence on the phone, the
    /// command having no effect on an application that has already
    /// left. But it is one more round trip over the connection, and at
    /// the application's closing these round trips are paid out of a
    /// counted budget.
    /// </summary>
    internal bool ClaimAppStop() => Interlocked.Exchange(ref _appStopClaimed, 1) == 0;

    private const int MaxRetainedLines = 60;

    private readonly Queue<string> _output = new();

    internal void Record(string line)
    {
        lock (_output)
        {
            _output.Enqueue(line);

            while (_output.Count > MaxRetainedLines)
            {
                _output.Dequeue();
            }
        }
    }

    internal IProcessSession Process { get; }
}

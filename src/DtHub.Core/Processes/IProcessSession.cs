using System.Threading.Channels;

namespace DtHub.Core.Processes;

/// <summary>Stream a line of output comes from.</summary>
public enum ProcessStreamKind
{
    Output,
    Error,
}

/// <summary>A line read from the output of a running process.</summary>
public readonly record struct ProcessOutputLine(ProcessStreamKind Stream, string Text)
{
    public bool IsError => Stream == ProcessStreamKind.Error;
}

/// <summary>
/// A long-running process, whose output is read as it streams in.
/// This is what scrcpy needs: it runs as long as the window is
/// open, and it announces on its output the id of the display it
/// has just created.
/// </summary>
public interface IProcessSession : IAsyncDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    /// <summary>
    /// Exit code once the process has ended, <c>null</c> before.
    /// </summary>
    int? ExitCode { get; }

    /// <summary>
    /// Output lines, in arrival order. The channel completes when
    /// the process stops and its streams have been drained.
    /// </summary>
    ChannelReader<ProcessOutputLine> Output { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the process. Does not touch any child processes that
    /// are already detached: the ADB server is shared with the
    /// rest of the machine and must not go down with a session.
    /// </summary>
    void Kill();
}

/// <summary>Starts a long-running process.</summary>
public interface IProcessLauncher
{
    /// <exception cref="ProcessLaunchException">
    /// The process could not start.
    /// </exception>
    IProcessSession Start(ProcessRequest request);
}

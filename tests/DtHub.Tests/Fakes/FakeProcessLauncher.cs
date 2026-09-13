using System.Threading.Channels;

using DtHub.Core.Processes;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated process session: lines are pushed into it just as
/// scrcpy would, and we decide when it ends.
/// </summary>
public sealed class FakeProcessSession : IProcessSession
{
    private readonly Channel<ProcessOutputLine> _channel = Channel.CreateUnbounded<ProcessOutputLine>();
    private readonly TaskCompletionSource<int> _exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeProcessSession(int processId = 4242) => ProcessId = processId;

    public int ProcessId { get; }

    public bool HasExited => _exited.Task.IsCompleted;

    public int? ExitCode => _exited.Task.IsCompletedSuccessfully ? _exited.Task.Result : null;

    public ChannelReader<ProcessOutputLine> Output => _channel.Reader;

    public bool WasKilled { get; private set; }

    public bool WasDisposed { get; private set; }

    /// <summary>
    /// Pushes a line the way scrcpy would on its output.
    /// </summary>
    public FakeProcessSession Emit(string text, bool isError = false)
    {
        _channel.Writer.TryWrite(new ProcessOutputLine(
            isError ? ProcessStreamKind.Error : ProcessStreamKind.Output, text));

        return this;
    }

    /// <summary>
    /// Ends the simulated process and closes its output.
    /// </summary>
    public void Exit(int exitCode = 0)
    {
        _exited.TrySetResult(exitCode);
        _channel.Writer.TryComplete();
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _exited.Task.WaitAsync(cancellationToken);

    public void Kill()
    {
        WasKilled = true;
        Exit(-1);
    }

    public ValueTask DisposeAsync()
    {
        WasDisposed = true;
        Exit(-1);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Simulated launcher, which returns sessions prepared ahead of
/// time.
/// </summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly Queue<FakeProcessSession> _prepared = new();

    /// <summary>Requests received, in order.</summary>
    public List<ProcessRequest> Requests { get; } = [];

    /// <summary>Sessions returned, in order.</summary>
    public List<FakeProcessSession> Started { get; } = [];

    /// <summary>
    /// Exception to throw on the next start, if one is set.
    /// </summary>
    public ProcessLaunchException? LaunchError { get; set; }

    public FakeProcessLauncher Prepare(FakeProcessSession session)
    {
        _prepared.Enqueue(session);
        return this;
    }

    public IProcessSession Start(ProcessRequest request)
    {
        Requests.Add(request);

        if (LaunchError is not null)
        {
            throw LaunchError;
        }

        var session = _prepared.Count > 0 ? _prepared.Dequeue() : new FakeProcessSession();
        Started.Add(session);

        return session;
    }
}

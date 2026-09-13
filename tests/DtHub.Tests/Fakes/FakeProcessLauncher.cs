using System.Threading.Channels;

using DtHub.Core.Processes;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Session de processus simulée : on lui pousse des lignes comme le ferait
/// scrcpy, et on décide quand elle se termine.
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

    /// <summary>Pousse une ligne comme scrcpy le ferait sur sa sortie.</summary>
    public FakeProcessSession Emit(string text, bool isError = false)
    {
        _channel.Writer.TryWrite(new ProcessOutputLine(
            isError ? ProcessStreamKind.Error : ProcessStreamKind.Output, text));

        return this;
    }

    /// <summary>Termine le processus simulé et referme sa sortie.</summary>
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

/// <summary>Lanceur simulé, qui rend des sessions préparées à l'avance.</summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly Queue<FakeProcessSession> _prepared = new();

    /// <summary>Requêtes reçues, dans l'ordre.</summary>
    public List<ProcessRequest> Requests { get; } = [];

    /// <summary>Sessions rendues, dans l'ordre.</summary>
    public List<FakeProcessSession> Started { get; } = [];

    /// <summary>Exception à lever au prochain démarrage, si elle est renseignée.</summary>
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

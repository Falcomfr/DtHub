using System.Threading.Channels;

namespace DtHub.Core.Processes;

/// <summary>
/// Nonexistent process session, used to represent a launch that
/// failed before it even started. Avoids having to handle null
/// references in session tracking.
/// </summary>
public sealed class NullProcessSession : IProcessSession
{
    public static readonly NullProcessSession Instance = new();

    private static readonly Channel<ProcessOutputLine> ClosedChannel = CreateClosedChannel();

    private NullProcessSession() { }

    public int ProcessId => 0;

    public bool HasExited => true;

    public int? ExitCode => -1;

    public ChannelReader<ProcessOutputLine> Output => ClosedChannel.Reader;

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) => Task.FromResult(-1);

    public void Kill() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Channel<ProcessOutputLine> CreateClosedChannel()
    {
        var channel = Channel.CreateUnbounded<ProcessOutputLine>();
        channel.Writer.Complete();
        return channel;
    }
}

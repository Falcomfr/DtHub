using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

using DtHub.Core.Localization;
using DtHub.Core.Processes;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Durable process whose output is published line by line into a
/// channel. No console window appears, and both streams are read
/// continuously so the child process is never blocked on a full
/// buffer.
/// </summary>
public sealed class ProcessSession : IProcessSession
{
    private readonly Process _process;
    private readonly Channel<ProcessOutputLine> _channel;
    private readonly TaskCompletionSource<int> _exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _openStreams = 2;
    private int _disposed;

    private ProcessSession(Process process, Channel<ProcessOutputLine> channel)
    {
        _process = process;
        _channel = channel;
    }

    public int ProcessId { get; private set; }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // Silence is intentional: a process whose state can
                // no longer be read is no longer running. Answering
                // "alive" would keep the session around indefinitely.
                return true;
            }
        }
    }

    public int? ExitCode => _exited.Task.IsCompletedSuccessfully ? _exited.Task.Result : null;

    public ChannelReader<ProcessOutputLine> Output => _channel.Reader;

    /// <summary>
    /// Starts the process and immediately begins reading its output.
    /// </summary>
    public static ProcessSession Start(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in request.Environment)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var channel = Channel.CreateUnbounded<ProcessOutputLine>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });

        var session = new ProcessSession(process, channel);

        process.OutputDataReceived += (_, e) => session.Publish(ProcessStreamKind.Output, e.Data);
        process.ErrorDataReceived += (_, e) => session.Publish(ProcessStreamKind.Error, e.Data);
        process.Exited += (_, _) => session.OnExited();

        try
        {
            if (!process.Start())
            {
                throw new ProcessLaunchException(
                    request.FileName,
                    Strings.Format("ProcessCouldNotStart", request.FileName));
            }
        }
        catch (Exception exception) when (exception is not ProcessLaunchException)
        {
            process.Dispose();

            throw new ProcessLaunchException(
                request.FileName,
                Strings.Format("ProcessCouldNotStartWhy", request.FileName, exception.Message),
                exception);
        }

        session.ProcessId = process.Id;

        // Attached right away: if the application dies without
        // going through its clean shutdown, Windows will stop this
        // process along with it.
        ChildProcessJob.Adopt(process.Handle);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return session;
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) =>
        await _exited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                // Deliberately without the process tree: scrcpy may
                // have started the ADB server, which is shared with
                // the whole machine.
                _process.Kill(entireProcessTree: false);
            }
        }
        catch (InvalidOperationException)
        {
            // The process no longer exists.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Termination already in progress on the system side.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Kill();

        // Give the process time to hand back control before
        // releasing the handle, otherwise the return code would be
        // lost.
        try
        {
            await _exited.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The process did not hand back control: we release it
            // anyway.
        }

        _channel.Writer.TryComplete();
        _process.Dispose();
    }

    private void Publish(ProcessStreamKind stream, string? data)
    {
        if (data is null)
        {
            // End of a stream: the channel only closes once both are
            // exhausted, otherwise lines would be lost.
            if (Interlocked.Decrement(ref _openStreams) == 0)
            {
                _channel.Writer.TryComplete();
            }

            return;
        }

        _channel.Writer.TryWrite(new ProcessOutputLine(stream, data));
    }

    private void OnExited()
    {
        var code = -1;
        try
        {
            code = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // Code unavailable: we return -1, which counts as
            // failure.
        }

        _exited.TrySetResult(code);
    }
}

/// <summary>
/// Launcher for durable processes backed by <see cref="ProcessSession"/>.
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public IProcessSession Start(ProcessRequest request) => ProcessSession.Start(request);
}

using System.Diagnostics;
using System.Text;

using DtHub.Core.Localization;
using DtHub.Core.Processes;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Runs a process without ever making a console appear. Output is
/// read as it streams on two separate pipes, which avoids the classic
/// deadlock where the child process fills a buffer while the caller
/// waits for it to end.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
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

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        // Both streams end with a null-data event: we wait for these
        // two signals to be certain nothing was truncated.
        var outputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) => Append(e.Data, standardOutput, outputClosed);
        process.ErrorDataReceived += (_, e) => Append(e.Data, standardError, errorClosed);

        var stopwatch = Stopwatch.StartNew();

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
            throw new ProcessLaunchException(
                request.FileName,
                Strings.Format("ProcessCouldNotStartWhy", request.FileName, exception.Message),
                exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Closing standard input: without this, a tool that reads
        // stdin would wait indefinitely.
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process has already ended, nothing to close.
        }

        using var timeoutSource = new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        if (request.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            timeoutSource.CancelAfter(timeout);
        }

        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);

            // Cancellation requested by the caller is propagated; the
            // timeout is a normal result that the caller will know
            // how to interpret.
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        try
        {
            await Task.WhenAll(outputClosed.Task, errorClosed.Task)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A stream was not closed after the process died: we
            // keep what could be read rather than block the caller.
        }

        stopwatch.Stop();

        return new ProcessResult
        {
            ExitCode = timedOut ? -1 : SafeExitCode(process),
            StandardOutput = standardOutput.ToString(),
            StandardError = standardError.ToString(),
            Duration = stopwatch.Elapsed,
            TimedOut = timedOut,
        };
    }

    /// <summary>
    /// Same launch, but standard output is read as bytes.
    ///
    /// Neither <c>StandardOutputEncoding</c> nor
    /// <c>BeginOutputReadLine</c>: the former would decode as UTF-8,
    /// the latter would split into lines, and an image survives
    /// neither. We read the raw stream, and standard error stays text
    /// because that is what it carries.
    /// </summary>
    public async Task<ProcessBytes> RunForBytesAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
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

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

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
            throw new ProcessLaunchException(
                request.FileName,
                Strings.Format("ProcessCouldNotStartWhy", request.FileName, exception.Message),
                exception);
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process has already ended, nothing to close.
        }

        using var timeoutSource = new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        if (request.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            timeoutSource.CancelAfter(timeout);
        }

        using var buffer = new MemoryStream();

        var errorRead = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var timedOut = false;

        try
        {
            // Both streams are drained in parallel: reading one all
            // the way through while the other fills its buffer would
            // block the process.
            await process.StandardOutput.BaseStream
                .CopyToAsync(buffer, linkedSource.Token)
                .ConfigureAwait(false);

            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);

            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        var error = string.Empty;

        try
        {
            error = await errorRead.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            // Standard error was not closed: what we have is enough.
        }

        return new ProcessBytes
        {
            ExitCode = timedOut ? -1 : SafeExitCode(process),
            StandardOutput = timedOut ? [] : buffer.ToArray(),
            StandardError = error,
            TimedOut = timedOut,
        };
    }

    private static void Append(string? data, StringBuilder target, TaskCompletionSource closed)
    {
        if (data is null)
        {
            closed.TrySetResult();
            return;
        }

        lock (target)
        {
            target.Append(data).Append('\n');
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process no longer exists.
        }
        catch (NotSupportedException)
        {
            // Platform without a process tree.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Termination already in progress on the system side.
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // Deliberate silence: a process already cleaned up no
            // longer has an exit code. Minus one says "we don't
            // know", which no program returns, and the caller can
            // therefore distinguish it from a real code.
            return -1;
        }
    }
}

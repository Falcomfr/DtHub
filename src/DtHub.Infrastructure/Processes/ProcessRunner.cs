using System.Diagnostics;
using System.Text;

using DtHub.Core.Processes;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Exécute un processus sans jamais faire apparaître de console. La sortie est
/// lue au fil de l'eau sur deux flux séparés, ce qui évite le blocage classique
/// où le processus fils remplit un tampon pendant que l'appelant attend sa fin.
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

        // Les deux flux se terminent par un événement à données nulles : on
        // attend ces deux signaux pour être certain de n'avoir rien tronqué.
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
                    $"Le processus {request.FileName} n'a pas pu être démarré.");
            }
        }
        catch (Exception exception) when (exception is not ProcessLaunchException)
        {
            throw new ProcessLaunchException(
                request.FileName,
                $"Le processus {request.FileName} n'a pas pu être démarré : {exception.Message}",
                exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Refermer l'entrée standard : sans cela, un outil qui lit stdin
        // attendrait indéfiniment.
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Le processus s'est déjà terminé, rien à fermer.
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

            // L'annulation demandée par l'appelant remonte ; le dépassement de
            // délai est un résultat normal que l'appelant saura interpréter.
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
            // Un flux n'a pas été refermé après la mort du processus : on garde
            // ce qui a pu être lu plutôt que de bloquer l'appelant.
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
            // Le processus n'existe plus.
        }
        catch (NotSupportedException)
        {
            // Plateforme sans arbre de processus.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Terminaison déjà en cours côté système.
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
            return -1;
        }
    }
}

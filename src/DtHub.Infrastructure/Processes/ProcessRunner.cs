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

    /// <summary>
    /// Même lancement, mais la sortie standard est lue en octets.
    ///
    /// Ni <c>StandardOutputEncoding</c> ni <c>BeginOutputReadLine</c> : le
    /// premier décoderait en UTF-8, le second découperait en lignes, et une
    /// image ne survit ni à l'un ni à l'autre. On lit le flux brut, et l'erreur
    /// standard reste du texte parce que c'est ce qu'elle porte.
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

        using var buffer = new MemoryStream();

        var errorRead = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var timedOut = false;

        try
        {
            // Les deux flux sont vidés en parallèle : lire l'un jusqu'au bout
            // pendant que l'autre remplit son tampon bloquerait le processus.
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
            // L'erreur standard n'a pas été refermée : ce qu'on a suffit.
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

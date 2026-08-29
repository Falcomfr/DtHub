using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

using DtHub.Core.Processes;

namespace DtHub.Infrastructure.Processes;

/// <summary>
/// Processus durable dont la sortie est publiée ligne par ligne dans un canal.
/// Aucune console n'apparaît, et les deux flux sont lus en continu pour ne
/// jamais bloquer le processus fils sur un tampon plein.
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
                return true;
            }
        }
    }

    public int? ExitCode => _exited.Task.IsCompletedSuccessfully ? _exited.Task.Result : null;

    public ChannelReader<ProcessOutputLine> Output => _channel.Reader;

    /// <summary>Démarre le processus et commence aussitôt à lire ses sorties.</summary>
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
                    $"Le processus {request.FileName} n'a pas pu être démarré.");
            }
        }
        catch (Exception exception) when (exception is not ProcessLaunchException)
        {
            process.Dispose();

            throw new ProcessLaunchException(
                request.FileName,
                $"Le processus {request.FileName} n'a pas pu être démarré : {exception.Message}",
                exception);
        }

        session.ProcessId = process.Id;

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
                // Volontairement sans l'arbre de processus : scrcpy a pu
                // démarrer le serveur ADB, partagé avec toute la machine.
                _process.Kill(entireProcessTree: false);
            }
        }
        catch (InvalidOperationException)
        {
            // Le processus n'existe plus.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Terminaison déjà en cours côté système.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Kill();

        // Laisser au processus le temps de rendre la main avant de libérer le
        // handle, sans quoi le code de retour serait perdu.
        try
        {
            await _exited.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Le processus n'a pas rendu la main : on libère quand même.
        }

        _channel.Writer.TryComplete();
        _process.Dispose();
    }

    private void Publish(ProcessStreamKind stream, string? data)
    {
        if (data is null)
        {
            // Fin d'un flux : le canal ne se ferme que lorsque les deux sont
            // épuisés, sinon des lignes seraient perdues.
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
            // Code indisponible : on rend -1, ce qui vaut échec.
        }

        _exited.TrySetResult(code);
    }
}

/// <summary>Lanceur de processus durables adossé à <see cref="ProcessSession"/>.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public IProcessSession Start(ProcessRequest request) => ProcessSession.Start(request);
}

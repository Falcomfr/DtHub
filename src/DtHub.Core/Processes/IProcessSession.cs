using System.Threading.Channels;

namespace DtHub.Core.Processes;

/// <summary>Flux d'où provient une ligne de sortie.</summary>
public enum ProcessStreamKind
{
    Output,
    Error,
}

/// <summary>Une ligne lue sur la sortie d'un processus en cours.</summary>
public readonly record struct ProcessOutputLine(ProcessStreamKind Stream, string Text)
{
    public bool IsError => Stream == ProcessStreamKind.Error;
}

/// <summary>
/// Un processus qui dure, dont on lit la sortie au fil de l'eau. C'est ce
/// qu'il faut pour scrcpy : il tourne tant que la fenêtre est ouverte, et il
/// annonce sur sa sortie l'identifiant de l'afficheur qu'il vient de créer.
/// </summary>
public interface IProcessSession : IAsyncDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    /// <summary>Code de retour une fois le processus terminé, <c>null</c> avant.</summary>
    int? ExitCode { get; }

    /// <summary>
    /// Lignes de sortie, dans l'ordre d'arrivée. Le canal se termine quand le
    /// processus s'arrête et que ses flux sont vidés.
    /// </summary>
    ChannelReader<ProcessOutputLine> Output { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Termine le processus. Ne touche pas à ses éventuels processus enfants
    /// déjà détachés : le serveur ADB est partagé avec le reste de la machine
    /// et ne doit pas tomber avec une session.
    /// </summary>
    void Kill();
}

/// <summary>Démarre un processus qui dure.</summary>
public interface IProcessLauncher
{
    /// <exception cref="ProcessLaunchException">Le processus n'a pas pu démarrer.</exception>
    IProcessSession Start(ProcessRequest request);
}

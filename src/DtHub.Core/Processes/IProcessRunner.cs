namespace DtHub.Core.Processes;

/// <summary>
/// Point d'entrée unique pour lancer un processus externe. Toute la couche ADB
/// et scrcpy passe par cette interface, ce qui permet de simuler les sorties
/// dans les tests sans le moindre téléphone.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Exécute le processus et attend sa fin. Ne lève pas d'exception sur un
    /// code de retour non nul : c'est un résultat, pas une erreur technique.
    /// </summary>
    /// <exception cref="ProcessLaunchException">Le processus n'a pas pu démarrer.</exception>
    /// <exception cref="OperationCanceledException">Annulation demandée par l'appelant.</exception>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exécute le processus et rend sa sortie standard telle quelle, en octets.
    ///
    /// Une voie distincte, et non une option de la précédente : celle-ci décode
    /// en UTF-8 et découpe en lignes, ce qui mutile une image. Elle sert à un
    /// seul besoin, extraire un fichier d'une archive sur l'appareil, et il n'y
    /// a pas lieu de l'élargir sans raison.
    /// </summary>
    /// <exception cref="ProcessLaunchException">Le processus n'a pas pu démarrer.</exception>
    /// <exception cref="OperationCanceledException">Annulation demandée par l'appelant.</exception>
    Task<ProcessBytes> RunForBytesAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default);
}

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
}

namespace DtHub.Core.Processes;

/// <summary>
/// Le processus n'a pas pu être démarré : exécutable absent, droits
/// insuffisants, chemin invalide. Un code de retour non nul n'entre pas dans
/// cette catégorie et remonte via <see cref="ProcessResult"/>.
/// </summary>
public sealed class ProcessLaunchException : Exception
{
    public ProcessLaunchException(string fileName, string message, Exception? innerException = null)
        : base(message, innerException) => FileName = fileName;

    public ProcessLaunchException() { }

    public ProcessLaunchException(string message) : base(message) { }

    public ProcessLaunchException(string message, Exception innerException)
        : base(message, innerException) { }

    public string? FileName { get; }
}

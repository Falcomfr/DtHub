namespace DtHub.Core.Storage;

/// <summary>
/// Persistance d'un document de configuration. Un fichier illisible ne doit
/// jamais empêcher l'application de démarrer : l'implémentation met de côté le
/// fichier fautif et repart d'un document valide.
/// </summary>
public interface IDocumentStore<T>
    where T : class, new()
{
    /// <summary>Chemin du fichier géré, pour le diagnostic.</summary>
    string FilePath { get; }

    /// <summary>
    /// Charge le document. Rend un document neuf si le fichier est absent,
    /// vide ou illisible. Ne lève pas sur un contenu invalide.
    /// </summary>
    Task<T> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Écrit le document de façon atomique.</summary>
    Task SaveAsync(T document, CancellationToken cancellationToken = default);
}

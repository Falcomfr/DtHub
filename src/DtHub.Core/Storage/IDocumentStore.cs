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

    /// <summary>
    /// Le document tel qu'il s'écrirait sur le disque.
    ///
    /// Ici et non chez l'appelant : c'est le magasin qui connaît le format, et
    /// un second jeu d'options ailleurs finirait par diverger du premier.
    /// </summary>
    string Serialize(T document);

    /// <summary>
    /// Le document que porte ce texte, ou <c>null</c> s'il ne s'y trouve pas.
    /// Ne lève pas.
    /// </summary>
    T? Deserialize(string json);
}

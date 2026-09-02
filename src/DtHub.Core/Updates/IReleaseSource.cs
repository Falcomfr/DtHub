namespace DtHub.Core.Updates;

/// <summary>
/// D'où viennent les livraisons. Une interface pour que la décision de mettre à
/// jour se teste sans réseau.
/// </summary>
public interface IReleaseSource
{
    /// <summary>
    /// La dernière livraison publiée, ou <c>null</c> s'il n'y en a aucune, si
    /// le dépôt n'existe pas encore, ou si le réseau ne répond pas. Une mise à
    /// jour qui ne se vérifie pas n'est pas une panne : l'application continue.
    /// </summary>
    Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Le contenu d'un petit fichier de la livraison, l'empreinte.</summary>
    Task<string> ReadAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Télécharge l'exécutable vers un fichier, en annonçant sa progression de
    /// zéro à un.
    /// </summary>
    Task DownloadAsync(
        string url,
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

namespace DtHub.Core.Updates;

/// <summary>
/// Une livraison publiée, telle que le dépôt la décrit.
/// </summary>
/// <param name="Version">Sa version, tirée de l'étiquette.</param>
/// <param name="Notes">Ce qui change, écrit dans le corps de la livraison.</param>
/// <param name="DownloadUrl">L'adresse de l'exécutable.</param>
/// <param name="SizeBytes">Sa taille, pour dire l'attente avant de la subir.</param>
/// <param name="DigestUrl">
/// L'adresse du fichier d'empreinte qui l'accompagne. Un exécutable de
/// soixante mégaoctets qui remplace le nôtre ne s'exécute pas sur la foi d'un
/// téléchargement : son empreinte est vérifiée avant qu'il ne serve.
/// </param>
public sealed record AppRelease(
    Version Version,
    string Notes,
    string DownloadUrl,
    long SizeBytes,
    string DigestUrl);

namespace DtHub.Core.Apps;

/// <summary>
/// Source des vrais noms d'applications, indexés par nom de paquet. ADB seul
/// ne les expose pas : il faut interroger le gestionnaire de paquets côté
/// téléphone, ce que sait faire scrcpy.
/// </summary>
public interface IAppLabelProvider
{
    /// <summary>
    /// Noms connus pour cet appareil. Rendre un dictionnaire vide est une
    /// réponse valide : l'affichage retombe alors sur le nom de paquet.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        string serial,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Source vide, utilisée tant qu'aucun fournisseur n'est branché et comme
/// repli quand celui en place échoue.
/// </summary>
public sealed class NullAppLabelProvider : IAppLabelProvider
{
    public static readonly NullAppLabelProvider Instance = new();

    public Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal));
}

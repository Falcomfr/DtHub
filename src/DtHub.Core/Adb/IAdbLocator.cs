namespace DtHub.Core.Adb;

/// <summary>
/// Fournit le chemin absolu de l'exécutable ADB utilisé par DT Hub. Le PATH
/// n'est jamais consulté : on ne veut dépendre d'aucune installation tierce,
/// ni tomber sur une version incompatible.
/// </summary>
public interface IAdbLocator
{
    /// <summary>
    /// Retourne le chemin absolu d'ADB, en le mettant en place si nécessaire.
    /// </summary>
    /// <exception cref="AdbException">ADB est indisponible et n'a pas pu être installé.</exception>
    Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Chemin d'ADB s'il est déjà en place, sans rien télécharger : le chemin
    /// imposé dans les paramètres s'il existe encore, sinon la copie de
    /// DT Hub. Sert à savoir, au démarrage, s'il y a quelque chose à mettre en
    /// place avant d'ouvrir la première fenêtre.
    /// </summary>
    string? TryGetInstalledPath();
}

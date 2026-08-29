using DtHub.Core.Processes;

namespace DtHub.Core.Adb;

/// <summary>
/// Surface ADB utilisée par le reste de l'application. Tout passe par ici, ce
/// qui permet de rejouer n'importe quel scénario dans les tests sans matériel.
/// </summary>
public interface IAdbClient
{
    /// <summary>Démarre le serveur ADB s'il ne tourne pas déjà.</summary>
    Task StartServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Arrête le serveur ADB. À n'appeler que sur demande explicite de
    /// l'utilisateur : le serveur est partagé avec les autres outils de la
    /// machine, et l'arrêter les couperait aussi.
    /// </summary>
    Task StopServerAsync(CancellationToken cancellationToken = default);

    /// <summary>Version d'ADB, pour la page de diagnostic.</summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Liste les appareils vus par le serveur ADB.</summary>
    /// <param name="detailed">Ajoute modèle, produit et chemin USB.</param>
    Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(
        bool detailed = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exécute une commande ADB brute. <paramref name="serial"/> cible un
    /// appareil précis ; <c>null</c> vise le serveur.
    /// </summary>
    Task<ProcessResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exécute une commande dans le shell de l'appareil et rend la sortie.
    /// </summary>
    /// <exception cref="AdbException">La commande a échoué.</exception>
    Task<string> ShellAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lit les propriétés système de l'appareil via <c>getprop</c>.</summary>
    Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
        string serial,
        CancellationToken cancellationToken = default);

    /// <summary>Attend qu'un appareil passe à l'état prêt.</summary>
    /// <returns>Vrai si l'appareil est prêt avant l'expiration du délai.</returns>
    Task<bool> WaitForDeviceAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

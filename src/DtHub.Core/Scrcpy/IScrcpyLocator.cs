namespace DtHub.Core.Scrcpy;

/// <summary>
/// Fournit le chemin absolu de l'exécutable scrcpy utilisé par DT Hub, en le
/// mettant en place si nécessaire.
/// </summary>
public interface IScrcpyLocator
{
    /// <exception cref="Dependencies.DependencyProvisioningException">
    /// scrcpy est indisponible et n'a pas pu être installé.
    /// </exception>
    Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default);

    /// <summary>Version installée, pour la page de diagnostic.</summary>
    string Version { get; }
}

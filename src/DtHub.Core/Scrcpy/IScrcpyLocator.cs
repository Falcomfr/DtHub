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

    /// <summary>
    /// Chemin de scrcpy s'il est déjà en place, sans rien télécharger. Sert au
    /// démarrage : demander le chemin tout court déclenchait la mise en place
    /// de onze mégaoctets avant la première fenêtre, pour un ramassage de
    /// fenêtres restées qui n'a par construction rien à ramasser tant que
    /// scrcpy n'a jamais tourné.
    /// </summary>
    string? TryGetInstalledPath();

    /// <summary>Version installée, pour la page de diagnostic.</summary>
    string Version { get; }
}

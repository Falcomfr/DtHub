namespace DtHub.App;

/// <summary>
/// Accès au conteneur pour les rares cas où une fenêtre en ouvre une autre.
/// Volontairement minimal : tout le reste passe par l'injection de
/// dépendances ordinaire.
/// </summary>
public static class AppHost
{
    private static IServiceProvider? _services;

    /// <summary>Services de l'application.</summary>
    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("L'hôte n'est pas encore démarré.");

    internal static void Initialize(IServiceProvider services) => _services = services;
}

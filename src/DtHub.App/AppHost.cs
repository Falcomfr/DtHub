namespace DtHub.App;

/// <summary>
/// Access to the container for the rare cases where one window
/// opens another. Deliberately minimal: everything else goes
/// through ordinary dependency injection.
/// </summary>
public static class AppHost
{
    private static IServiceProvider? _services;

    /// <summary>Application services.</summary>
    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("L'hôte n'est pas encore démarré.");

    internal static void Initialize(IServiceProvider services) => _services = services;
}

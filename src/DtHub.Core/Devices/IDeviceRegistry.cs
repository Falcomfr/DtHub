namespace DtHub.Core.Devices;

/// <summary>
/// Mémoire des appareils entre deux lancements : noms personnalisés, dernière
/// adresse connue, appareil principal. Ne contient jamais de code d'appairage.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>Appareils mémorisés, connectés ou non.</summary>
    Task<IReadOnlyList<AndroidDevice>> GetKnownAsync(CancellationToken cancellationToken = default);

    /// <summary>Ajoute ou met à jour un appareil, en conservant les choix de l'utilisateur.</summary>
    Task UpsertAsync(AndroidDevice device, CancellationToken cancellationToken = default);

    /// <summary>Ajoute ou met à jour plusieurs appareils en une écriture.</summary>
    Task UpsertRangeAsync(IEnumerable<AndroidDevice> devices, CancellationToken cancellationToken = default);

    /// <summary>Oublie un appareil et tout ce qui le concerne.</summary>
    Task ForgetAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Renomme un appareil. Un nom vide rétablit le nom détecté.</summary>
    Task RenameAsync(string deviceId, string? customName, CancellationToken cancellationToken = default);

    /// <summary>Désigne l'appareil principal. Un seul à la fois.</summary>
    Task SetPrimaryAsync(string deviceId, CancellationToken cancellationToken = default);
}

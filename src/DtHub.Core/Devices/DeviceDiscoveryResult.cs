namespace DtHub.Core.Devices;

/// <summary>
/// Résultat d'un balayage. Les avertissements portent les incidents non
/// bloquants : un appareil dont les propriétés n'ont pas pu être lues apparaît
/// quand même, avec ce qu'on sait déjà de lui.
/// </summary>
public sealed record DeviceDiscoveryResult(
    IReadOnlyList<AndroidDevice> Devices,
    IReadOnlyList<string> Warnings)
{
    public static readonly DeviceDiscoveryResult Empty = new([], []);

    public IEnumerable<AndroidDevice> Connected => Devices.Where(d => d.IsConnected);
}

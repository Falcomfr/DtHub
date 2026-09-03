namespace DtHub.Core.Adb;

/// <summary>
/// Une ligne de <c>adb devices</c> ou <c>adb devices -l</c>, transcrite telle
/// quelle. C'est une vue brute du transport ADB : l'appareil enrichi par
/// <c>getprop</c> et par les réglages de l'utilisateur est un autre modèle.
/// </summary>
public sealed record AdbDeviceEntry
{
    /// <summary>Numéro de série ADB, ou <c>adresse:port</c> en sans-fil.</summary>
    public required string Serial { get; init; }

    public required AdbDeviceState State { get; init; }

    /// <summary>Libellé d'état brut, utile lorsque <see cref="State"/> vaut Unknown.</summary>
    public required string RawState { get; init; }

    public AdbConnectionKind ConnectionKind { get; init; }

    public string? Product { get; init; }
    public string? Model { get; init; }
    public string? Device { get; init; }
    public string? TransportId { get; init; }

    /// <summary>Chemin USB rapporté par <c>adb devices -l</c>, par exemple <c>1-2</c>.</summary>
    public string? UsbPath { get; init; }

    /// <summary>Hôte extrait du numéro de série en connexion sans fil.</summary>
    public string? Host { get; init; }

    /// <summary>Port extrait du numéro de série en connexion sans fil.</summary>
    public int? Port { get; init; }

    /// <summary>Vrai si l'appareil accepte des commandes maintenant.</summary>
    public bool IsReady => State == AdbDeviceState.Device;

    /// <summary>Nom lisible de repli tant que <c>getprop</c> n'a pas répondu.</summary>
    public string DisplayName => Model?.Replace('_', ' ') ?? Serial;
}

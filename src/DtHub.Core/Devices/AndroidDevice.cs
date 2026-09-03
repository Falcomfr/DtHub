using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// Un téléphone connu de DT Hub, qu'il soit branché ou non. Le modèle réunit
/// ce qu'ADB rapporte, ce que le téléphone déclare et ce que l'utilisateur a
/// choisi de retenir.
/// </summary>
public sealed record AndroidDevice
{
    /// <summary>
    /// Identité stable, indépendante du mode de connexion. Le numéro de série
    /// ADB ne convient pas : il devient une adresse dès que le téléphone passe
    /// en Wi-Fi, et changerait à chaque bail DHCP.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Numéro de série ADB courant, ou dernier connu si hors ligne.</summary>
    public required string Serial { get; init; }

    public AdbDeviceState State { get; init; } = AdbDeviceState.Unknown;

    public AdbConnectionKind ConnectionKind { get; init; } = AdbConnectionKind.Unknown;

    public string? Manufacturer { get; init; }

    /// <summary>Modèle technique, par exemple <c>23078RKD5G</c>.</summary>
    public string? Model { get; init; }

    /// <summary>Nom commercial quand le constructeur le publie.</summary>
    public string? MarketName { get; init; }

    /// <summary>Nom de code interne, par exemple <c>aristotle</c>.</summary>
    public string? DeviceCodename { get; init; }

    /// <summary>Version Android affichable, par exemple <c>14</c>.</summary>
    public string? AndroidVersion { get; init; }

    /// <summary>Niveau d'API.</summary>
    public int? SdkVersion { get; init; }

    /// <summary>Nom donné par l'utilisateur, prioritaire sur tout le reste.</summary>
    public string? CustomName { get; init; }

    /// <summary>Dernière adresse Wi-Fi vue, réutilisée pour la reconnexion.</summary>
    public string? LastKnownAddress { get; init; }

    /// <summary>Dernier port de connexion sans fil observé.</summary>
    public int? LastKnownPort { get; init; }

    /// <summary>Vrai si l'appareil a déjà été appairé en Wi-Fi.</summary>
    public bool IsPaired { get; init; }

    /// <summary>Appareil mis en avant dans l'interface.</summary>
    public bool IsPrimary { get; init; }

    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>Prêt à recevoir des commandes maintenant.</summary>
    public bool IsConnected => State == AdbDeviceState.Device;

    /// <summary>
    /// Nom affiché. Le choix de l'utilisateur prime, sinon le nom commercial,
    /// sinon constructeur et modèle, et en dernier recours le numéro de série.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomName))
            {
                return CustomName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(MarketName))
            {
                return MarketName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Model))
            {
                var model = Model.Trim();

                // Éviter « Google Google Pixel 9 » quand le modèle porte déjà
                // le nom du constructeur.
                return !string.IsNullOrWhiteSpace(Manufacturer)
                       && !model.StartsWith(Manufacturer, StringComparison.OrdinalIgnoreCase)
                    ? $"{Manufacturer.Trim()} {model}"
                    : model;
            }

            return Serial;
        }
    }

    /// <summary>Adresse complète de reconnexion, si elle est connue.</summary>
    public string? ReconnectAddress =>
        LastKnownAddress is { Length: > 0 } address && LastKnownPort is > 0
            ? $"{address}:{LastKnownPort}"
            : null;
}

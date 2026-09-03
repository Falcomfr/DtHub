namespace DtHub.Core.Devices;

/// <summary>
/// Lecture des propriétés système d'un téléphone. Chaque information utile
/// existe sous plusieurs clés selon le constructeur : on les essaie dans
/// l'ordre plutôt que de supposer un fabricant particulier.
/// </summary>
public static class DeviceProperties
{
    /// <summary>
    /// Numéro de série matériel. C'est la seule identité qui survit au passage
    /// de l'USB au Wi-Fi, où le numéro de série ADB devient une adresse.
    /// </summary>
    public static readonly string[] SerialKeys =
    [
        "ro.serialno",
        "ro.boot.serialno",
        "ro.kernel.androidboot.serialno",
    ];

    /// <summary>
    /// Nom commercial. Absent chez plusieurs constructeurs, d'où le repli
    /// final sur le modèle technique.
    /// </summary>
    public static readonly string[] MarketNameKeys =
    [
        "ro.product.marketname",
        "ro.vendor.product.marketname",
        "ro.product.vendor.marketname",
        "ro.product.odm.marketname",
        "ro.config.marketing_name",
        "ro.oppo.market.name",
    ];

    public static readonly string[] ManufacturerKeys =
    [
        "ro.product.manufacturer",
        "ro.product.vendor.manufacturer",
        "ro.product.system.manufacturer",
    ];

    public static readonly string[] ModelKeys =
    [
        "ro.product.model",
        "ro.product.vendor.model",
        "ro.product.system.model",
    ];

    public static readonly string[] DeviceKeys =
    [
        "ro.product.device",
        "ro.product.vendor.device",
        "ro.build.product",
    ];

    public const string AndroidReleaseKey = "ro.build.version.release";
    public const string SdkVersionKey = "ro.build.version.sdk";

    /// <summary>
    /// Première valeur non vide parmi les clés proposées. Les valeurs de
    /// remplissage renvoyées par Android, comme <c>unknown</c>, sont écartées.
    /// </summary>
    public static string? FirstValue(
        IReadOnlyDictionary<string, string>? properties,
        params string[] keys)
    {
        if (properties is null || keys is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (properties.TryGetValue(key, out var value) && IsMeaningful(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>Numéro de série matériel, ou <c>null</c> s'il est masqué.</summary>
    public static string? ReadHardwareSerial(IReadOnlyDictionary<string, string>? properties) =>
        FirstValue(properties, SerialKeys);

    public static string? ReadManufacturer(IReadOnlyDictionary<string, string>? properties) =>
        FirstValue(properties, ManufacturerKeys);

    public static string? ReadModel(IReadOnlyDictionary<string, string>? properties) =>
        FirstValue(properties, ModelKeys);

    public static string? ReadDeviceCodename(IReadOnlyDictionary<string, string>? properties) =>
        FirstValue(properties, DeviceKeys);

    public static string? ReadMarketName(IReadOnlyDictionary<string, string>? properties) =>
        FirstValue(properties, MarketNameKeys);

    public static string? ReadAndroidRelease(IReadOnlyDictionary<string, string>? properties) =>
        FirstValue(properties, AndroidReleaseKey);

    /// <summary>Niveau d'API, ou <c>null</c> si la propriété est absente ou illisible.</summary>
    public static int? ReadSdkVersion(IReadOnlyDictionary<string, string>? properties)
    {
        var raw = FirstValue(properties, SdkVersionKey);

        return int.TryParse(raw, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var sdk) && sdk > 0
            ? sdk
            : null;
    }

    private static bool IsMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(value, "0", StringComparison.Ordinal);
}

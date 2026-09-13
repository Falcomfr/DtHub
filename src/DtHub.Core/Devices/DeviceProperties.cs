namespace DtHub.Core.Devices;

/// <summary>
/// Reading a phone's system properties. Each useful piece of
/// information exists under several keys depending on the
/// manufacturer: they are tried in order rather than assuming a
/// particular manufacturer.
/// </summary>
public static class DeviceProperties
{
    /// <summary>
    /// Hardware serial number. It is the only identity that
    /// survives the move from USB to Wi-Fi, where the ADB serial
    /// number becomes an address.
    /// </summary>
    public static readonly string[] SerialKeys =
    [
        "ro.serialno",
        "ro.boot.serialno",
        "ro.kernel.androidboot.serialno",
    ];

    /// <summary>
    /// Commercial name. Missing for several manufacturers, hence
    /// the final fallback to the technical model.
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
    /// First non-empty value among the proposed keys. Placeholder
    /// values returned by Android, such as <c>unknown</c>, are
    /// discarded.
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

    /// <summary>
    /// Hardware serial number, or <c>null</c> if it is hidden.
    /// </summary>
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

    /// <summary>
    /// API level, or <c>null</c> if the property is missing or
    /// unreadable.
    /// </summary>
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

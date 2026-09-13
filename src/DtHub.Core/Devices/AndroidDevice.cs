using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// A phone known to DT Hub, whether plugged in or not. The model
/// brings together what ADB reports, what the phone declares, and
/// what the user has chosen to keep.
/// </summary>
public sealed record AndroidDevice
{
    /// <summary>
    /// Stable identity, independent of the connection mode. The ADB
    /// serial number does not work: it becomes an address as soon as
    /// the phone switches to Wi-Fi, and would change with every DHCP
    /// lease.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Current ADB serial number, or last known one if offline.
    /// </summary>
    public required string Serial { get; init; }

    public AdbDeviceState State { get; init; } = AdbDeviceState.Unknown;

    public AdbConnectionKind ConnectionKind { get; init; } = AdbConnectionKind.Unknown;

    public string? Manufacturer { get; init; }

    /// <summary>Technical model, for example <c>23078RKD5G</c>.</summary>
    public string? Model { get; init; }

    /// <summary>Market name when the manufacturer publishes one.</summary>
    public string? MarketName { get; init; }

    /// <summary>Internal codename, for example <c>aristotle</c>.</summary>
    public string? DeviceCodename { get; init; }

    /// <summary>Displayable Android version, for example <c>14</c>.</summary>
    public string? AndroidVersion { get; init; }

    /// <summary>API level.</summary>
    public int? SdkVersion { get; init; }

    /// <summary>
    /// Name given by the user, taking priority over everything else.
    /// </summary>
    public string? CustomName { get; init; }

    /// <summary>Last Wi-Fi address seen, reused for reconnection.</summary>
    public string? LastKnownAddress { get; init; }

    /// <summary>Last observed wireless connection port.</summary>
    public int? LastKnownPort { get; init; }

    /// <summary>
    /// True if the device has already been paired over Wi-Fi.
    /// </summary>
    public bool IsPaired { get; init; }

    /// <summary>Device highlighted in the interface.</summary>
    public bool IsPrimary { get; init; }

    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>Ready to receive commands now.</summary>
    public bool IsConnected => State == AdbDeviceState.Device;

    /// <summary>
    /// Displayed name. The user's choice takes priority, otherwise
    /// the market name, otherwise manufacturer and model, and as a
    /// last resort the serial number.
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

                // Avoid "Google Google Pixel 9" when the model
                // already carries the manufacturer's name.
                return !string.IsNullOrWhiteSpace(Manufacturer)
                       && !model.StartsWith(Manufacturer, StringComparison.OrdinalIgnoreCase)
                    ? $"{Manufacturer.Trim()} {model}"
                    : model;
            }

            return Serial;
        }
    }

    /// <summary>Full reconnection address, if known.</summary>
    public string? ReconnectAddress =>
        LastKnownAddress is { Length: > 0 } address && LastKnownPort is > 0
            ? $"{address}:{LastKnownPort}"
            : null;
}

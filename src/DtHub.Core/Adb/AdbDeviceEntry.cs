namespace DtHub.Core.Adb;

/// <summary>
/// A line from <c>adb devices</c> or <c>adb devices -l</c>,
/// transcribed as is. This is a raw view of the ADB transport: the
/// device enriched by <c>getprop</c> and by the user's settings is a
/// different model.
/// </summary>
public sealed record AdbDeviceEntry
{
    /// <summary>
    /// ADB serial number, or <c>address:port</c> over wireless.
    /// </summary>
    public required string Serial { get; init; }

    public required AdbDeviceState State { get; init; }

    /// <summary>
    /// Raw state label, useful when <see cref="State"/> is Unknown.
    /// </summary>
    public required string RawState { get; init; }

    public AdbConnectionKind ConnectionKind { get; init; }

    public string? Product { get; init; }
    public string? Model { get; init; }
    public string? Device { get; init; }
    public string? TransportId { get; init; }

    /// <summary>
    /// USB path reported by <c>adb devices -l</c>, for example
    /// <c>1-2</c>.
    /// </summary>
    public string? UsbPath { get; init; }

    /// <summary>
    /// Host extracted from the serial number over a wireless
    /// connection.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// Port extracted from the serial number over a wireless
    /// connection.
    /// </summary>
    public int? Port { get; init; }

    /// <summary>True if the device accepts commands now.</summary>
    public bool IsReady => State == AdbDeviceState.Device;

    /// <summary>
    /// Readable fallback name until <c>getprop</c> has answered.
    /// </summary>
    public string DisplayName => Model?.Replace('_', ' ') ?? Serial;
}

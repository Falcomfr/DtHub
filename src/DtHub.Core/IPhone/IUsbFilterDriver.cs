namespace DtHub.Core.IPhone;

/// <summary>How an install or uninstall of the USB filter ended.</summary>
public enum UsbFilterResult
{
    Done,

    /// <summary>The player declined the Windows elevation prompt.</summary>
    Refused,

    Failed,
}

/// <summary>
/// The libusb-win32 filter on the player's Apple devices, installed only
/// on request and only ever on an Apple composite parent.
/// </summary>
public interface IUsbFilterDriver
{
    /// <summary>The ids <c>USB\VID_05AC&amp;PID_xxxx</c> of the present Apple composite parents.</summary>
    IReadOnlyList<string> PresentAppleDevices();

    /// <summary>True when every present parent with this id is on usbccgp and has libusb0 among its UpperFilters.</summary>
    bool IsFiltered(string hardwareId);

    Task<UsbFilterResult> InstallAsync(IReadOnlyList<string> hardwareIds, CancellationToken cancellationToken);

    Task<UsbFilterResult> UninstallAsync(IReadOnlyList<string> hardwareIds, CancellationToken cancellationToken);

    /// <summary>The provisioned <c>libusb0.dll</c>, or null if it is not there.</summary>
    string? LibUsb0Path();
}

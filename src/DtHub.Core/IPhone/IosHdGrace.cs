namespace DtHub.Core.IPhone;

/// <summary>
/// Starting the HD picture switches the device's USB configuration, which
/// takes it out of usbmux's list for a few seconds: not an unplug.
/// </summary>
public static class IosHdGrace
{
    // Two re-enumerations, each up to 10 s, when a killed tool left the capture shown.
    private static readonly TimeSpan Span = TimeSpan.FromSeconds(30);

    /// <summary>True while an absent device is still within the switch's grace.</summary>
    public static bool Tolerates(DateTime startedUtc, DateTime nowUtc) => nowUtc - startedUtc < Span;
}

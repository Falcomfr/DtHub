namespace DtHub.Core.IPhone;

/// <summary>
/// Starting the HD picture switches the device's USB configuration, which
/// takes it out of usbmux's list for a few seconds: not an unplug.
/// </summary>
public static class IosHdGrace
{
    private static readonly TimeSpan Span = TimeSpan.FromSeconds(15);

    /// <summary>True while an absent device is still within the switch's grace.</summary>
    public static bool Tolerates(DateTime startedUtc, DateTime nowUtc) => nowUtc - startedUtc < Span;
}

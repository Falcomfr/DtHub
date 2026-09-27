using DtHub.Core.Adb;

namespace DtHub.Core.Guidance;

/// <summary>
/// What to tell someone whose phone has stopped answering.
///
/// **The advice used to be the same whatever the phone was attached
/// by**, and it was written for wireless: be on the same Wi-Fi network
/// as this PC, have wireless debugging on. A phone at the end of a
/// cable that drops therefore sent its owner to look at a network that
/// had nothing to do with it, while the likeliest cause, a cable that
/// only carries charge, was never named. The two failures share
/// nothing, so neither do the words.
///
/// Only keys are returned here, resolved by the caller: this is how
/// the choice can be checked in the three languages at once, which is
/// where the mistake was hiding.
/// </summary>
public static class OfflineAdvice
{
    /// <summary>
    /// The short line shown under the phone, in place of nothing.
    ///
    /// It has to hold on one line, next to the button that opens the
    /// full steps: a sentence under every unreachable phone would take
    /// the list's room to say what the tooltip already says at length.
    /// </summary>
    public static string HowKey(AdbConnectionKind connection) => connection switch
    {
        AdbConnectionKind.Usb => "OfflineHowUsb",
        AdbConnectionKind.Wireless => "OfflineHowWireless",
        _ => "OfflineHowUnknown",
    };

    /// <summary>
    /// The longer explanation, on hover.
    ///
    /// The emulator falls in with the unknown, and that is deliberate:
    /// it is outside the functional scope, so promising it a cable or a
    /// network would be inventing an answer.
    /// </summary>
    public static string TipKey(AdbConnectionKind connection) => connection switch
    {
        AdbConnectionKind.Usb => "OfflineTipUsb",
        AdbConnectionKind.Wireless => "OfflineTipWireless",
        _ => "OfflineTip",
    };
}

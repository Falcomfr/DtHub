using System.Windows;
using System.Windows.Media;

using DtHub.Core.Settings;

namespace DtHub.App.Services;

/// <summary>
/// Which brush an account's colour wears.
///
/// It returns a resource key and not a brush, following what the device
/// rows already do for their status and their gauge. The palette then
/// stays the only place a colour is written down, and nothing here
/// needs a dynamic reference on a frozen brush, which is the mistake
/// that once turned the whole interface black.
/// </summary>
public static class AccountTints
{
    /// <summary>
    /// The palette key for a colour, or <c>null</c> when the account has
    /// none.
    ///
    /// The keys run in the enum's declaration order, so adding a colour
    /// means adding a brush beside the others and nothing else. A closed
    /// enum and a total function: this cannot fail and fall back to the
    /// converter's grey.
    /// </summary>
    public static string? KeyFor(AccountColour? colour) => colour switch
    {
        AccountColour.Teal => "AccountTintA",
        AccountColour.Moss => "AccountTintB",
        AccountColour.Brass => "AccountTintC",
        AccountColour.Indigo => "AccountTintD",
        AccountColour.Orchid => "AccountTintE",
        AccountColour.Rose => "AccountTintF",

        // None and null both land here: neither wears a tint.
        _ => null,
    };

    /// <summary>
    /// The same colour as Windows wants it for a window frame, or
    /// <c>null</c> to hand the frame back to the system.
    ///
    /// COLORREF is 0x00BBGGRR and not RGB. Getting the order wrong
    /// gives a plausible colour rather than an error, which is the
    /// worst kind of mistake to go looking for, so the swap happens
    /// once, here.
    ///
    /// The value is read from the palette rather than written a second
    /// time: one hexadecimal per tint, in the dictionary, even when it
    /// ends up crossing into Win32.
    /// </summary>
    public static int? FrameColourRefFor(AccountColour? colour)
    {
        if (KeyFor(colour) is not { } key
            || Application.Current?.TryFindResource(key) is not SolidColorBrush brush)
        {
            return null;
        }

        var c = brush.Color;

        return c.R | (c.G << 8) | (c.B << 16);
    }
}

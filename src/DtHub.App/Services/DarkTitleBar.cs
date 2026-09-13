using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DtHub.App.Services;

/// <summary>
/// Applies the dark title bar to windows that keep Windows' own one.
///
/// The application has only one palette, dark. The configurator and
/// the guide windows draw their own frame, but the eight dialog
/// boxes kept the system's, which renders in light mode: a pale band
/// above a black body, on half the product's windows.
///
/// The attribute is set once and for all by a class handler, not
/// window by window: the ones written later will have it without
/// anyone having to think about it. Windows with their own frame
/// have no bar to tint, the call costs them nothing and does nothing
/// to them.
/// </summary>
internal static class DarkTitleBar
{
    /// <summary>
    /// The attribute number changed along the way: 19 on Windows 10
    /// versions before 2004, 20 since. The declared baseline goes
    /// down to 1809, so both are tried. An unknown number returns an
    /// error that is ignored, which is the right behavior: the
    /// window stays light.
    /// </summary>
    private const int DarkModeBefore20H1 = 19;
    private const int DarkMode = 20;

    /// <summary>
    /// Wires up the tinting on every window of the application.
    /// </summary>
    public static void Arm() =>
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply(sender as Window)));

    private static void Apply(Window? window)
    {
        if (window is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var on = 1;

        if (DwmSetWindowAttribute(handle, DarkMode, ref on, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, DarkModeBefore20H1, ref on, sizeof(int));
        }
    }

    // DllImport and not LibraryImport: the latter requires unsafe
    // code for a parameter passed by reference, and the project does
    // not allow that. It is also the form used by the other forty
    // one calls in the repository.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window, int attribute, ref int value, int size);
}

namespace System.Windows.Interop;

/// <summary>
/// The native handle of one of our windows, as the shared code asks WPF for
/// it: here its CGWindowID, the handle <see cref="DtHub.Mac.Platform.MacWindowController"/>
/// works with. Zero until the window is shown.
/// </summary>
public sealed class WindowInteropHelper(Avalonia.Controls.Window window)
{
    public nint Handle =>
        window.TryGetPlatformHandle() is { Handle: not 0 } native
            ? (nint)DtHub.Mac.Platform.MacNative.dthub_window_number(native.Handle)
            : 0;
}

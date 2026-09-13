using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

using DtHub.Core.Settings;
using DtHub.Core.Windows;

using Serilog;

namespace DtHub.App.Services;

/// <summary>
/// Remembers and restores the position of the application's
/// windows.
///
/// Through the Windows API rather than WPF's properties: the
/// latter are expressed in the scale of the screen carrying the
/// window, so the same number does not designate the same spot
/// from one screen to another. Moving a window to a second screen
/// of a different density, then reopening it, used to bring it
/// back somewhere else. The API, for its part, works in desktop
/// coordinates and brings back by itself, onto a screen that is
/// present, a window that was saved on a screen since unplugged.
/// </summary>
public sealed class WindowPlacements(IWindowController windows, SettingsService settings)
{
    /// <summary>Quest tracking.</summary>
    public const string Quests = "quests";

    /// <summary>The window for pages opened from a guide.</summary>
    public const string LinkedPage = "page";

    /// <summary>Today's Almanax.</summary>
    public const string Almanax = "almanax";

    /// <summary>The settings panel.</summary>
    public const string Configurator = "configurator";

    /// <summary>
    /// The tabbed frame. The key comes from the settings, where
    /// profiles keep it too: two spellings would have produced two
    /// entries.
    /// </summary>
    public const string Tabs = SettingsService.TabsPlacementKey;

    private readonly IWindowController _windows = windows;
    private readonly SettingsService _settings = settings;

    /// <summary>
    /// Puts a window back where it was, and says whether it could
    /// be.
    ///
    /// Call this only once the window is connected to Windows,
    /// never before: without a handle, there is nothing to place.
    /// </summary>
    public bool Restore(Window window, string key, AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(document);

        if (!document.WindowPlacements.TryGetValue(key, out var placement))
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;

        if (handle == 0 || !_windows.SetPlacement(handle, placement))
        {
            return false;
        }

        // Twice, and the second time once the message loop has
        // passed.
        //
        // The first call moves the window, which makes it change
        // screen and therefore density. The size, meanwhile, was
        // just applied at the starting screen's density, and WPF
        // rescales it while absorbing the change: measured on a
        // second screen at a hundred and fifty percent, an 800 x
        // 620 window came back as 533 x 413. The second call, once
        // the window has arrived, gives the correct size. The
        // position, for its part, was right from the first call.
        _ = window.Dispatcher.BeginInvoke(
            new Action(() => _windows.SetPlacement(handle, placement)),
            DispatcherPriority.Loaded);

        return true;
    }

    /// <summary>
    /// Remembers where a window is. No effect if it no longer
    /// exists.
    /// </summary>
    public async Task SaveAsync(Window? window, string key)
    {
        if (window is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;

        if (handle == 0)
        {
            return;
        }

        try
        {
            await _settings
                .SetWindowPlacementAsync(key, _windows.GetPlacement(handle))
                .ConfigureAwait(true);
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "La place de la fenêtre {Fenetre} n'a pas pu être retenue.", key);
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "La place de la fenêtre {Fenetre} n'a pas pu être retenue.", key);
        }
    }
}

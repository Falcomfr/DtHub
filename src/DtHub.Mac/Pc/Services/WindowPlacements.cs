using System.IO;

using Avalonia.Controls;

using DtHub.Core.Settings;
using DtHub.Core.Windows;

using Serilog;

namespace DtHub.App.Services;

/// <summary>
/// The PC's WindowPlacements (src/DtHub.App/Services/WindowPlacements.cs)
/// for Avalonia windows: where each of DT Hub's own windows was left, kept
/// in the settings under the same keys. The PC version reapplies the
/// placement once the window has changed screen density, a WPF quirk
/// macOS does not have.
/// </summary>
public sealed class WindowPlacements(IWindowController windows, SettingsService settings)
{
    public const string Quests = "quests";

    public const string LinkedPage = "page";

    public const string Almanax = "almanax";

    public const string Map = "map";

    public const string Calcofus = "calcofus";

    public const string DofusRoom = "dofusroom";

    public const string PapychaQuests = "papychaquests";

    public const string Lomitall = "lomitall";

    public const string Configurator = "configurator";

    public const string Tabs = SettingsService.TabsPlacementKey;

    private readonly IWindowController _windows = windows;
    private readonly SettingsService _settings = settings;

    public bool Restore(Window window, string key, AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(document);

        if (!document.WindowPlacements.TryGetValue(key, out var placement))
        {
            return false;
        }

        // The tabbed frame's window is only its strip: the placement, the
        // whole frame's, is the frame's to apply, games included.
        if (window is DtHub.App.Windows.TabbedGameWindow frame)
        {
            return frame.RestorePlacement(placement);
        }

        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        return handle != 0 && _windows.SetPlacement(handle, placement);
    }

    /// <summary>
    /// Puts the window at its saved place as it is first shown, before
    /// macOS draws it: without this it shows at its default place, then
    /// jumps where Restore puts it once open. Restore still runs then, and
    /// settles the few points the title bar's height can differ by. The
    /// key is read at that moment, null leaving the window where it would
    /// open; only a document already in memory is used, never waited for.
    /// </summary>
    public void PlaceBeforeShow(Window window, Func<string?> key)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(key);

        void OnShown(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Avalonia.Visual.IsVisibleProperty || e.NewValue is not true)
            {
                return;
            }

            window.PropertyChanged -= OnShown;

            if (key() is { } name)
            {
                Preset(window, name);
            }
        }

        window.PropertyChanged += OnShown;
    }

    private void Preset(Window window, string key)
    {
        var reading = _settings.GetAsync();

        if (!reading.IsCompletedSuccessfully
            || !reading.Result.WindowPlacements.TryGetValue(key, out var placement)
            || !placement.IsSized)
        {
            return;
        }

        // In points, top-left origin, as MacWindowController keeps it;
        // Avalonia's screens are in pixels, each at its own scale.
        var left = (double)placement.Left;
        var top = (double)placement.Top;
        var width = placement.Right - placement.Left;
        var height = placement.Bottom - placement.Top;

        var screens = window.Screens.All;
        var screen = screens.FirstOrDefault(s =>
        {
            var b = s.Bounds;
            var scale = s.Scaling;
            return left < (b.X + b.Width) / scale && b.X / scale < left + width
                && top < (b.Y + b.Height) / scale && b.Y / scale < top + height;
        });

        // A screen since unplugged: on the main one, as SetPlacement does.
        if (screen is null && window.Screens.Primary is { } primary)
        {
            screen = primary;
            left = primary.WorkingArea.X / primary.Scaling;
            top = primary.WorkingArea.Y / primary.Scaling;
        }

        var factor = screen?.Scaling ?? 1;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = width;
        window.Height = Math.Max(1, height - DtHub.Mac.Platform.MacWindowController.TitleBarHeight);
        window.Position = new Avalonia.PixelPoint((int)Math.Round(left * factor), (int)Math.Round(top * factor));
    }

    public async Task SaveAsync(Window? window, string key)
    {
        if (window is null)
        {
            return;
        }

        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;

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

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

        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        return handle != 0 && _windows.SetPlacement(handle, placement);
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

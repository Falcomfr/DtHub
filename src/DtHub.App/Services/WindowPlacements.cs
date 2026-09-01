using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

using DtHub.Core.Settings;
using DtHub.Core.Windows;

using Serilog;

namespace DtHub.App.Services;

/// <summary>
/// Retient et restitue la place des fenêtres de l'application.
///
/// Par l'API de Windows plutôt que par les propriétés de WPF : celles-ci sont
/// exprimées dans l'échelle de l'écran qui porte la fenêtre, si bien qu'un même
/// chiffre ne désigne pas le même endroit d'un écran à l'autre. Déplacer une
/// fenêtre sur un second écran d'une autre densité, puis la rouvrir, la
/// ramenait ailleurs. L'API, elle, travaille en coordonnées du bureau et ramène
/// d'elle-même sur un écran présent une fenêtre enregistrée sur un écran depuis
/// débranché.
/// </summary>
public sealed class WindowPlacements(IWindowController windows, SettingsService settings)
{
    /// <summary>Le suivi de quêtes.</summary>
    public const string Quests = "quests";

    /// <summary>La fenêtre des pages ouvertes depuis un guide.</summary>
    public const string LinkedPage = "page";

    /// <summary>Le panneau de réglages.</summary>
    public const string Configurator = "configurator";

    private readonly IWindowController _windows = windows;
    private readonly SettingsService _settings = settings;

    /// <summary>
    /// Remet une fenêtre où elle était, et dit si elle a pu l'être.
    ///
    /// À appeler une fois la fenêtre reliée à Windows, jamais avant : sans
    /// poignée, il n'y a rien à placer.
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

        // Deux fois, et la seconde une fois la boucle de messages passée.
        //
        // Le premier appel déplace la fenêtre, ce qui la fait changer d'écran
        // donc de densité. La taille, elle, vient d'être appliquée dans la
        // densité de l'écran de départ, et WPF la reproportionne en encaissant
        // le changement : mesuré sur un second écran à cent cinquante pour
        // cent, une fenêtre de 800 x 620 revenait à 533 x 413. Le second appel,
        // la fenêtre étant arrivée, rend la bonne taille. La position, elle,
        // était juste dès le premier.
        _ = window.Dispatcher.BeginInvoke(
            new Action(() => _windows.SetPlacement(handle, placement)),
            DispatcherPriority.Loaded);

        return true;
    }

    /// <summary>Retient où est une fenêtre. Sans effet si elle n'existe plus.</summary>
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

using System.Windows;

using DtHub.App.Services;
using DtHub.Core.Dofus;
using DtHub.Core.Settings;

using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// The interactive map of the DOFUS Touch resources, first of the DOFUS
/// Touch tools. A web site, shown in DT Hub's own engine like the guides.
/// </summary>
public partial class MapWindow : Window
{
    /// <summary>The share of the work area the window takes the first time.</summary>
    private const double FirstShare = 0.65;

    private readonly WindowPlacements _placements;
    private readonly SettingsService _settings;
    private readonly WebViewEnvironment _engine;

    public MapWindow(WindowPlacements placements, SettingsService settings, WebViewEnvironment engine)
    {
        InitializeComponent();

        _placements = placements;
        _settings = settings;
        _engine = engine;

        Loaded += async (_, _) => await OpenAsync().ConfigureAwait(true);
    }

    private async Task OpenAsync()
    {
        try
        {
            await View.EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true)).ConfigureAwait(true);
            View.CoreWebView2.Navigate(DofusTouchMap.Address);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La carte n'a pas pu s'ouvrir.");
        }
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var document = await _settings.GetAsync().ConfigureAwait(true);

            if (!_placements.Restore(this, WindowPlacements.Map, document))
            {
                var area = SystemParameters.WorkArea;

                Width = area.Width * FirstShare;
                Height = area.Height * FirstShare;
                Left = area.Left + ((area.Width - Width) / 2);
                Top = area.Top + ((area.Height - Height) / 2);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La place de la carte n'a pas pu être rétablie.");
        }
    }

    /// <summary>Remembered here: the handle is gone by OnClosed.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _ = _placements.SaveAsync(this, WindowPlacements.Map);

        base.OnClosing(e);
    }
}

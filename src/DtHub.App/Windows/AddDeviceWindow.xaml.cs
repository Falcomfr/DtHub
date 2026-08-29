using System.Windows;
using System.Windows.Threading;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>
/// Fenêtre d'ajout d'un appareil. Elle se rafraîchit toute seule et tente les
/// connexions d'office : dans le cas courant, l'utilisateur n'a rien à faire
/// d'autre que la regarder trouver son téléphone.
/// </summary>
public partial class AddDeviceWindow : Window
{
    private readonly AddDeviceViewModel _viewModel;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };

    public AddDeviceWindow(AddDeviceViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.ScanAsync(CancellationToken.None).ConfigureAwait(true);
            _poll.Start();
        };

        _poll.Tick += async (_, _) => await _viewModel.ScanAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Retient le téléphone choisi pour l'association. Un bouton radio dans un
    /// modèle de données n'expose pas directement son élément à la vue-modèle.
    /// </summary>
    private void OnPairableChecked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { DataContext: DeviceEntryViewModel entry })
        {
            _viewModel.SelectedPairable = entry;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        base.OnClosed(e);
    }
}

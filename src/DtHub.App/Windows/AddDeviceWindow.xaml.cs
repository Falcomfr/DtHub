using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using DtHub.App.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace DtHub.App.Windows;

/// <summary>
/// Fenêtre d'association d'un téléphone neuf. Elle surveille le réseau : le
/// téléphone apparaît dès que l'écran d'association est ouvert, sans rien
/// saisir d'autre que le code.
/// </summary>
public partial class AddDeviceWindow : Window
{
    private readonly AddDeviceViewModel _viewModel;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };

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

    /// <summary>Ouvre l'aide, où la marche à suivre s'adapte à la marque.</summary>
    private void OnHelp(object sender, RoutedEventArgs e)
    {
        var help = AppHost.Services.GetRequiredService<HelpWindow>();
        help.Owner = this;
        help.ShowDialog();
    }

    /// <summary>
    /// Retient le téléphone choisi. Un bouton radio dans un modèle de données
    /// n'expose pas directement son élément à la vue-modèle.
    /// </summary>
    private void OnCandidateChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: PairingCandidateViewModel candidate })
        {
            _viewModel.SelectedCandidate = candidate;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        base.OnClosed(e);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        _viewModel.DevicePaired += OnDevicePaired;

        Loaded += async (_, _) =>
        {
            await _viewModel.ScanAsync(CancellationToken.None).ConfigureAwait(true);
            _poll.Start();

            Code.Focus();
        };

        _poll.Tick += async (_, _) => await _viewModel.ScanAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Entrée associe, depuis le champ du code. C'est le premier écran qu'un
    /// nouvel utilisateur rencontre : on y tape six chiffres relevés sur le
    /// téléphone, et jusqu'ici la touche Entrée n'y faisait rien du tout.
    ///
    /// Un bouton par défaut ne conviendrait pas : la fenêtre porte deux champs
    /// et deux boutons, exclusifs par leur visibilité, et un IsDefault unique
    /// ne saurait pas lequel des deux servir.
    /// </summary>
    private void OnCodeKey(object sender, KeyEventArgs e) =>
        Run(e, _viewModel.CanPair, _viewModel.PairCommand);

    /// <summary>Entrée connecte, depuis le champ du port.</summary>
    private void OnPortKey(object sender, KeyEventArgs e) =>
        Run(e, _viewModel.CanConnect, _viewModel.ConnectCommand);

    private static void Run(KeyEventArgs e, bool allowed, System.Windows.Input.ICommand command)
    {
        if (e.Key != Key.Enter || !allowed)
        {
            return;
        }

        e.Handled = true;
        command.Execute(null);
    }

    /// <summary>
    /// L'association a réussi : la fenêtre n'a plus de raison d'être. Le court
    /// délai laisse voir le message de confirmation avant qu'elle disparaisse.
    /// </summary>
    private void OnDevicePaired(object? sender, EventArgs e)
    {
        _poll.Stop();

        var closing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };

        closing.Tick += (_, _) =>
        {
            closing.Stop();
            Close();
        };

        closing.Start();
    }

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
        _viewModel.DevicePaired -= OnDevicePaired;

        base.OnClosed(e);
    }
}

using System.Windows;
using System.Windows.Threading;

using DtHub.App.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace DtHub.App.Windows;

/// <summary>
/// Fenêtre de mise en route, montrée au tout premier lancement. Elle surveille
/// les téléphones en continu : brancher un câble suffit à les voir apparaître,
/// il n'y a pas de bouton « ajouter un appareil ».
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SetupViewModel _viewModel;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };

    public SetupWindow(SetupViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        viewModel.CloseRequested += OnCloseRequested;

        Loaded += async (_, _) =>
        {
            await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
            _poll.Start();
        };

        _poll.Tick += async (_, _) => await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Vrai si l'utilisateur a validé son choix.</summary>
    public bool Confirmed => _viewModel.IsConfirmed;

    /// <summary>Ouvre la fenêtre d'ajout, puis rafraîchit la liste.</summary>
    private async void OnAddDevice(object sender, RoutedEventArgs e)
    {
        var dialog = AppHost.Services.GetRequiredService<AddDeviceWindow>();
        dialog.Owner = this;
        dialog.ShowDialog();

        await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnCloseRequested(object? sender, bool confirmed)
    {
        _poll.Stop();
        DialogResult = confirmed;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        _viewModel.CloseRequested -= OnCloseRequested;

        base.OnClosed(e);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using DtHub.App.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// Window for pairing a new phone. It watches the network: the
/// phone appears as soon as the pairing screen is open, without
/// entering anything other than the code.
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

        // Bounded for the same reason as the panel's tick: without
        // this, an unexpected fault during the phone search would
        // open an error window every two seconds, on top of the
        // window where the pairing code is typed.
        _poll.Tick += async (_, _) =>
        {
            try
            {
                await _viewModel.ScanAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Log.Warning(exception, "La recherche de téléphones a échoué.");
            }
        };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Enter pairs, from the code field. This is the first screen a
    /// new user encounters: six digits read off the phone are typed
    /// here, and until now the Enter key did nothing at all there.
    ///
    /// A default button would not work: the window carries two
    /// fields and two buttons, mutually exclusive by their
    /// visibility, and a single IsDefault would not know which of
    /// the two to serve.
    /// </summary>
    private void OnCodeKey(object sender, KeyEventArgs e) =>
        Run(e, _viewModel.CanPair, _viewModel.PairCommand);

    /// <summary>Enter connects, from the port field.</summary>
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
    /// Pairing succeeded: the window no longer has a reason to
    /// exist. The short delay lets the confirmation message be seen
    /// before it disappears.
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

    /// <summary>
    /// Opens help, where the steps to follow adapt to the brand.
    /// </summary>
    private void OnHelp(object sender, RoutedEventArgs e)
    {
        var help = AppHost.Services.GetRequiredService<HelpWindow>();
        help.Owner = this;
        help.ShowDialog();
    }

    /// <summary>
    /// Remembers the chosen phone. A radio button inside a data
    /// template does not directly expose its item to the view model.
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

using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using DtHub.App.ViewModels;
using DtHub.Mac.Pc;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace DtHub.Mac.Views;

/// <summary>
/// Window for pairing a new phone, the PC's AddDeviceWindow
/// (src/DtHub.App/Windows/AddDeviceWindow.xaml.cs). It watches the network:
/// the phone appears as soon as its pairing screen is open, and nothing
/// but the code is typed.
/// </summary>
public partial class AddDeviceWindow : Window
{
    private readonly AddDeviceViewModel _viewModel = null!;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _scanning;

    /// <summary>
    /// Set on close: the first scan can outlast the window, adb taking up
    /// to forty-five seconds to start, and its end would restart the timer.
    /// </summary>
    private bool _closed;

    /// <summary>For the XAML previewer only.</summary>
    public AddDeviceWindow() => AvaloniaXamlLoader.Load(this);

    public AddDeviceWindow(AddDeviceViewModel viewModel)
    {
        _viewModel = viewModel;

        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;

        _viewModel.DevicePaired += OnDevicePaired;

        Opened += async (_, _) =>
        {
            this.FindControl<TextBox>("Code")?.Focus();

            try
            {
                await _viewModel.ScanAsync(CancellationToken.None).ConfigureAwait(true);
                await _viewModel.WatchCableAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Log.Warning(exception, "La recherche de téléphones a échoué.");
            }

            if (_closed)
            {
                return;
            }

            _poll.Start();
        };

        // Bounded: without this, an unexpected fault during the phone
        // search would open an error every two seconds, on top of the
        // window where the pairing code is typed.
        _poll.Tick += async (_, _) =>
        {
            // One search at a time: adb can take twenty seconds to answer,
            // forty-five while its server starts.
            if (_scanning)
            {
                return;
            }

            _scanning = true;

            try
            {
                await _viewModel.ScanAsync(CancellationToken.None).ConfigureAwait(true);
                await _viewModel.WatchCableAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Log.Warning(exception, "La recherche de téléphones a échoué.");
            }
            finally
            {
                _scanning = false;
            }
        };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Enter pairs, from the code field: the first screen a new player
    /// meets, six digits read off the phone. A default button would not
    /// do: the window carries two fields and two buttons, shown in turn.
    /// </summary>
    private void OnCodeKey(object? sender, KeyEventArgs e) =>
        Run(e, _viewModel.CanPair, _viewModel.PairCommand);

    /// <summary>Enter connects, from the port field.</summary>
    private void OnPortKey(object? sender, KeyEventArgs e) =>
        Run(e, _viewModel.CanConnect, _viewModel.ConnectCommand);

    private static void Run(KeyEventArgs e, bool allowed, ICommand command)
    {
        if (e.Key != Key.Enter || !allowed)
        {
            return;
        }

        e.Handled = true;
        command.Execute(null);
    }

    /// <summary>
    /// Pairing succeeded: the window has no reason left to exist. The short
    /// delay lets the confirmation be read before it goes.
    /// </summary>
    private void OnDevicePaired(object? sender, EventArgs e)
    {
        _poll.Stop();
        DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(1200));
    }

    /// <summary>Opens the help, where the steps adapt to the brand.</summary>
    private void OnHelp(object? sender, RoutedEventArgs e) => ShowHelp(fixes: false);

    /// <summary>"Nothing shows up?": the help, straight on its fixes.</summary>
    private void OnFixes(object? sender, RoutedEventArgs e) => ShowHelp(fixes: true);

    private async void ShowHelp(bool fixes)
    {
        var help = MacApp.Services.GetRequiredService<HelpWindow>();
        help.OpenOn(_viewModel.ByCable, fixes);
        _ = await Modal.ShowOver<object>(help, this).ConfigureAwait(true);
    }

    /// <summary>
    /// Remembers the chosen phone: a radio button inside a data template
    /// does not hand its item to the view model by itself.
    /// </summary>
    private void OnCandidateChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, DataContext: PairingCandidateViewModel candidate })
        {
            _viewModel.SelectedCandidate = candidate;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _poll.Stop();
        _viewModel.DevicePaired -= OnDevicePaired;

        base.OnClosed(e);
    }
}

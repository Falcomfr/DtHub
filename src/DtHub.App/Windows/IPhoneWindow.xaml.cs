using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

using DtHub.App.Services;
using DtHub.Core.IPhone;
using DtHub.Core.Localization;
using DtHub.Infrastructure.IPhone;

namespace DtHub.App.Windows;

/// <summary>
/// The iPhones and iPads plugged into the PC, each with a button that
/// opens its screen. Written on 2026-11-04 without an iOS 27
/// device at hand, see D227.
/// </summary>
public partial class IPhoneWindow : Window
{
    private readonly PyMobileDevice _tools;
    private readonly WebViewEnvironment _engine;

    // Closing the list stops its search or its opening.
    private readonly CancellationToken _closing;

    // One action at a time in this window; PyMobileDevice serializes the
    // tool itself across windows.
    private bool _busy;

    public IPhoneWindow(PyMobileDevice tools, WebViewEnvironment engine)
    {
        _tools = tools;
        _engine = engine;

        InitializeComponent();

        Loaded += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        var closing = new CancellationTokenSource();
        _closing = closing.Token;
        Closed += (_, _) =>
        {
            closing.Cancel();
            closing.Dispose();
        };
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync().ConfigureAwait(true);

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async Task RefreshAsync()
    {
        if (!Begin(Strings.Get("IPhonePreparing")))
        {
            return;
        }

        try
        {
            var devices = await _tools.ListAsync(_closing).ConfigureAwait(true);
            ShowRows(devices);
            Status.Text = devices.Count == 0 ? Strings.Get("IPhoneNone") : string.Empty;
        }
        catch (IPhoneException exception)
        {
            // Shown to the player: it says what to do.
            Status.Text = exception.Message;
        }
        catch (OperationCanceledException)
        {
            // The window was closed during the search.
        }
        finally
        {
            End();
        }
    }

    private async Task OpenAsync(IPhoneDevice device)
    {
        if (!Begin(Strings.Format("IPhoneStarting", device.Name)))
        {
            return;
        }

        try
        {
            var port = FreePort();
            var session = await _tools.StartMirrorAsync(device, port, _closing).ConfigureAwait(true);

            try
            {
                // Not owned: it stays open when this list is closed.
                new IPhoneMirrorWindow(device, port, session, _engine).Show();
            }
            catch
            {
                // Without its window, nothing would ever stop the stream.
                await session.DisposeAsync().ConfigureAwait(true);
                throw;
            }

            Status.Text = string.Empty;
        }
        catch (IPhoneException exception)
        {
            // Shown to the player: it says what to do.
            Status.Text = exception.Message;
        }
        catch (OperationCanceledException)
        {
            // The window was closed during the opening.
        }
        finally
        {
            End();
        }
    }

    private void ShowRows(IReadOnlyList<IPhoneDevice> devices)
    {
        Rows.Children.Clear();

        foreach (var device in devices)
        {
            var open = new Button { Content = Strings.Get("IPhoneOpen"), MinWidth = 90, IsEnabled = device.CanMirror };
            open.Click += async (_, _) => await OpenAsync(device).ConfigureAwait(true);

            var label = new TextBlock
            {
                Text = device.CanMirror
                    ? $"{device.Name} · iOS {device.IosVersion}"
                    : $"{device.Name} · {Strings.Format("IPhoneTooOld", device.IosVersion)}",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            };

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            row.Children.Add(label);
            Rows.Children.Add(row);
        }
    }

    private bool Begin(string status)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        Status.Text = status;
        RefreshButton.IsEnabled = false;
        Rows.IsEnabled = false;
        return true;
    }

    private void End()
    {
        _busy = false;
        RefreshButton.IsEnabled = true;
        Rows.IsEnabled = true;
    }

    /// <summary>A port nothing listens on, for this iPhone's viewer page.</summary>
    private static int FreePort()
    {
        // shortcut: the port is free when picked, not guaranteed when
        // serve-web binds it a few seconds later; let the tool pick and
        // print it if a player ever hits "address in use".
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

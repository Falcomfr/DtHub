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
/// opens its screen. Trial: written on 2026-11-04 without an iOS 27
/// device at hand, see D227.
/// </summary>
public partial class IPhoneWindow : Window
{
    private readonly PyMobileDevice _tools;
    private readonly WebViewEnvironment _engine;

    // One tool call at a time: the first one installs uv and Python, and
    // two in parallel would download them twice into the same folder.
    private bool _busy;

    public IPhoneWindow(PyMobileDevice tools, WebViewEnvironment engine)
    {
        _tools = tools;
        _engine = engine;

        InitializeComponent();

        Loaded += async (_, _) => await RefreshAsync().ConfigureAwait(true);
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
            var devices = await _tools.ListAsync(CancellationToken.None).ConfigureAwait(true);
            ShowRows(devices);
            Status.Text = devices.Count == 0 ? Strings.Get("IPhoneNone") : string.Empty;
        }
        catch (IPhoneException exception)
        {
            // Shown to the player: it says what to do.
            Status.Text = exception.Message;
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
            var session = await _tools.StartMirrorAsync(device, port, CancellationToken.None).ConfigureAwait(true);

            // Not owned: it stays open when this list is closed.
            new IPhoneMirrorWindow(device, port, session, _engine).Show();
            Status.Text = string.Empty;
        }
        catch (IPhoneException exception)
        {
            // Shown to the player: it says what to do.
            Status.Text = exception.Message;
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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

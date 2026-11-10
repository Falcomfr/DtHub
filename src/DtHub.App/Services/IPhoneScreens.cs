using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core.IPhone;
using DtHub.Infrastructure.IPhone;

using Microsoft.Extensions.Logging;

namespace DtHub.App.Services;

/// <summary>
/// The iPhones of the device list and their open screens, one window per
/// iPhone (D227). Used on the UI thread only.
/// </summary>
public sealed partial class IPhoneScreens(PyMobileDevice tools, WebViewEnvironment engine, ILogger<IPhoneScreens> logger)
{
    // usbmux list starts Python: not at every sweep of the list.
    private static readonly TimeSpan ListEvery = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, IPhoneMirrorWindow> _open = new(StringComparer.Ordinal);
    private readonly HashSet<string> _opening = new(StringComparer.Ordinal);
    private DateTime _listed = DateTime.MinValue;
    private bool _listing;

    public ObservableCollection<IPhoneRowViewModel> Devices { get; } = [];

    /// <summary>A screen opened or closed: the arranging buttons count them.</summary>
    public event EventHandler? Changed;

    /// <summary>Screens shown, counted by the arranging buttons.</summary>
    public int OpenCount => _open.Count;

    /// <summary>
    /// The screens shown, out of maximized, as handle and picture shape: the
    /// grid places them after the phones'.
    /// </summary>
    public IReadOnlyList<(nint Handle, double Aspect)> TakeScreensForArranging()
    {
        foreach (var window in _open.Values)
        {
            window.WindowState = System.Windows.WindowState.Normal;
        }

        return [.. _open.Values.Where(w => w.Handle != 0).Select(w => (w.Handle, w.Aspect))];
    }

    /// <summary>Looks for iPhones, once the iPhone tools were set up.</summary>
    public async Task RefreshAsync()
    {
        if (_listing || !tools.IsInstalled || DateTime.UtcNow - _listed < ListEvery)
        {
            return;
        }

        _listing = true;

        try
        {
            Show(await tools.ListAsync(CancellationToken.None).ConfigureAwait(true));
        }
        catch (IPhoneException exception)
        {
            // Apple Devices closed, say: the list just shows no iPhone.
            LogListFailed(exception.Message);
            Show([]);
        }
        finally
        {
            _listed = DateTime.UtcNow;
            _listing = false;
        }
    }

    /// <summary>Shows the iPhones found, and asks the next sweep to look again.</summary>
    public void Show(IReadOnlyList<IPhoneDevice> devices)
    {
        // An open screen keeps its row while the iPhone is briefly not listed.
        var rows = devices
            .Select(d => Devices.FirstOrDefault(r => r.Device == d) ?? new IPhoneRowViewModel(d) { IsOpen = _open.ContainsKey(d.Udid) })
            .Concat(Devices.Where(r => r.IsOpen && devices.All(d => d.Udid != r.Device.Udid)))
            .ToList();

        if (!rows.SequenceEqual(Devices))
        {
            Devices.Clear();

            foreach (var row in rows)
            {
                Devices.Add(row);
            }
        }
    }

    /// <summary>Opens the iPhone's screen, or brings it forward.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task OpenAsync(IPhoneDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (_open.TryGetValue(device.Udid, out var shown))
        {
            shown.Activate();
            return;
        }

        if (!_opening.Add(device.Udid))
        {
            return;
        }

        try
        {
            var port = FreePort();
            var session = await tools.StartMirrorAsync(device, port, cancellationToken).ConfigureAwait(true);
            IPhoneMirrorWindow window;

            try
            {
                window = new IPhoneMirrorWindow(device, port, session, engine);
            }
            catch
            {
                // Without its window, nothing would ever stop the stream.
                await session.DisposeAsync().ConfigureAwait(true);
                throw;
            }

            _open[device.Udid] = window;
            window.Closed += (_, _) =>
            {
                _open.Remove(device.Udid);
                SetOpen(device.Udid, false);
            };
            window.Show();
            SetOpen(device.Udid, true);
        }
        finally
        {
            _opening.Remove(device.Udid);
        }
    }

    public void Close(string udid)
    {
        if (_open.TryGetValue(udid, out var window))
        {
            window.Close();
        }
    }

    private void SetOpen(string udid, bool open)
    {
        Changed?.Invoke(this, EventArgs.Empty);

        foreach (var row in Devices.Where(r => r.Device.Udid == udid))
        {
            row.IsOpen = open;
        }

        if (!open)
        {
            // An unplugged iPhone's row goes once its screen is closed.
            _listed = DateTime.MinValue;
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Recherche des iPhone impossible : {message}")]
    private partial void LogListFailed(string message);
}

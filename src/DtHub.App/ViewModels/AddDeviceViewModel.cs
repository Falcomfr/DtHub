using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>A phone that displays a pairing code.</summary>
public sealed partial class PairingCandidateViewModel : ObservableObject
{
    public PairingCandidateViewModel(MdnsService service) => Service = service;

    public MdnsService Service { get; private set; }

    public string Address => Service.Address;

    /// <summary>
    /// Readable name. The announcement has the form
    /// <c>adb-&lt;serial&gt;-&lt;random&gt;</c>: the serial number is
    /// enough to tell two phones apart.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var parts = Service.Name.Split('-');
            return parts.Length >= 2 ? parts[1] : Service.Name;
        }
    }

    /// <summary>
    /// Takes up the current announcement of the same phone.
    ///
    /// The name does not move from one announcement to the next, the address
    /// does: the phone draws a fresh port every time the code screen is
    /// reopened, and the DHCP lease may have given it another address
    /// meanwhile.
    /// </summary>
    public void Update(MdnsService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        Service = service;

        OnPropertyChanged(nameof(Address));
    }
}

/// <summary>
/// Window for pairing a new phone. It serves only that purpose: the
/// connection of already paired phones happens on its own, in the main
/// window.
/// </summary>
public sealed partial class AddDeviceViewModel : ObservableObject
{
    private readonly DevicePairingService _pairing;
    private readonly IDeviceRegistry _registry;

    /// <summary>
    /// Host of the phone that just accepted the code, as long as it is
    /// not yet connected.
    ///
    /// The accepted code is not enough to play: the phone still has to
    /// announce itself on the network and be connected to. This
    /// announcement often arrives after the attempt has already
    /// returned control, and the window then stayed open forever on a
    /// pairing that had in fact succeeded. Observed on this machine,
    /// two pairings back to back because the first was never seen to
    /// go through.
    /// </summary>
    private string? _awaitedHost;

    public AddDeviceViewModel(DevicePairingService pairing, IDeviceRegistry registry)
    {
        _pairing = pairing;
        _registry = registry;
    }

    /// <summary>Phones that display a pairing code.</summary>
    public ObservableCollection<PairingCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty]
    private PairingCandidateViewModel? _selectedCandidate;

    [ObservableProperty]
    private string _pairingCode = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// True when pairing has succeeded but the connection port still
    /// needs to be entered. That is when, and only when, the field
    /// appears.
    /// </summary>
    [ObservableProperty]
    private bool _needsPort;

    /// <summary>The port the user reads on their phone.</summary>
    [ObservableProperty]
    private string _connectPort = string.Empty;

    /// <summary>
    /// True when the announced address does not answer and the phone's own is
    /// needed. That is when, and only when, the field appears.
    /// </summary>
    [ObservableProperty]
    private bool _needsAddress;

    /// <summary>
    /// The "IP address and port" the user reads on the code screen. Once
    /// filled in, it takes precedence over the network's announcement.
    /// </summary>
    [ObservableProperty]
    private string _pairingAddress = string.Empty;

    /// <summary>Raised after a successful pairing.</summary>
    public event EventHandler? DevicePaired;

    public bool CanPair => SelectedCandidate is not null && PairingCode.Trim().Length > 0 && !IsBusy;

    public bool CanConnect =>
        SelectedCandidate is not null
        && !IsBusy
        && int.TryParse(ConnectPort.Trim(), out var port)
        && port is > 0 and <= 65535;

    /// <summary>
    /// Looks for phones displaying a code. Called in a loop: the phone
    /// appears as soon as the pairing screen is opened.
    /// </summary>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Has the paired phone announced itself since? This scan
            // already runs every two seconds: asking it costs nothing,
            // and it is what was missing to close the window.
            if (_awaitedHost is { Length: > 0 } awaited
                && await ConnectedSinceAsync(awaited, cancellationToken).ConfigureAwait(true))
            {
                _awaitedHost = null;

                Settle(new WirelessPairingResult(
                    WirelessPairingStatus.Connected,
                    Strings.Get("PairedAndConnected")));

                return;
            }

            var found = await _pairing.FindPairingCandidatesAsync(cancellationToken).ConfigureAwait(true);

            foreach (var service in found)
            {
                // An already listed candidate is taken up, not left as it was.
                // It used to be left alone, and the address kept was the one
                // of the very first announcement seen: the failure said
                // "expired code", the user reopened the code screen on the
                // phone, which drew a fresh port the window never saw.
                // Measured on the machine, a pairing turned impossible while
                // aiming at 192.168.1.16:43415 when the phone announced .23.
                var candidate = Candidates.FirstOrDefault(
                    c => string.Equals(c.Service.Name, service.Name, StringComparison.Ordinal));

                if (candidate is null)
                {
                    Candidates.Add(new PairingCandidateViewModel(service));
                }
                else
                {
                    candidate.Update(service);
                }
            }

            foreach (var stale in Candidates
                .Where(c => !found.Any(s => string.Equals(s.Name, c.Service.Name, StringComparison.Ordinal)))
                .ToList())
            {
                Candidates.Remove(stale);
            }

            // A single phone waiting is selected automatically; all
            // that remains then is to enter the code.
            if (SelectedCandidate is null || !Candidates.Contains(SelectedCandidate))
            {
                SelectedCandidate = Candidates.FirstOrDefault();
            }
        }
        catch (AdbException exception)
        {
            Status = exception.UserMessage;
        }
    }

    /// <summary>
    /// True if the expected phone has finally announced itself and
    /// accepts the connection.
    ///
    /// The host is compared, not just "something got connected":
    /// another already paired phone can announce itself at the same
    /// moment, and closing the window because of it would wrongly
    /// suggest that the pairing had just succeeded.
    /// </summary>
    private async Task<bool> ConnectedSinceAsync(string host, CancellationToken cancellationToken)
    {
        var announced = await _pairing.FindConnectableAsync(cancellationToken).ConfigureAwait(true);

        if (announced.FirstOrDefault(
                s => string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase)) is not { } mine)
        {
            return false;
        }

        var result = await _pairing.ConnectAsync(mine.Host, mine.Port, cancellationToken)
            .ConfigureAwait(true);

        return result.Connected;
    }

    [RelayCommand]
    private async Task PairAsync(CancellationToken cancellationToken)
    {
        if (SelectedCandidate is not { } candidate)
        {
            Status = Strings.Get("NoPairingCodeYet");
            return;
        }

        var code = PairingCode.Trim();
        if (code.Length == 0)
        {
            Status = Strings.Get("EnterSixDigitCode");
            return;
        }

        IsBusy = true;
        Status = Strings.Get("PairingInProgress");
        OnPropertyChanged(nameof(CanPair));

        try
        {
            var (host, port) = AddressToUse(candidate);

            var result = await _pairing
                .PairAndConnectAsync(host, port, code, cancellationToken)
                .ConfigureAwait(true);

            // A code serves once, so it is wiped at once. Except when the
            // address never answered: the phone then received nothing, the
            // code still holds, and making the user type it again while it
            // runs towards its expiry would be losing it for good.
            if (!result.NeedsAddress)
            {
                PairingCode = string.Empty;
            }

            // An accepted pairing lifts the exclusion, and only that:
            // it is the explicit gesture by which one comes back from
            // a break. Without this, a device set aside would stay
            // refused even though its code had just been retyped, and
            // nothing would say so.
            if (result.Paired
                && MdnsDeviceName.HardwareSerialFromInstance(candidate.Service.Name) is { Length: > 0 } serial)
            {
                await _registry.WelcomeBackAsync(serial, cancellationToken).ConfigureAwait(true);
            }

            Settle(result);

            // The code was accepted but the connection never came: we watch
            // for the phone's announcement instead of leaving the window open.
            // What is watched is the address that actually served, not the one
            // the network announced: those are sometimes two different things.
            _awaitedHost = result.Paired && !result.Connected ? host : null;
        }
        catch (AdbException exception)
        {
            Status = exception.UserMessage;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanPair));
        }
    }

    /// <summary>
    /// The address to submit to pairing: the one the user read on the phone
    /// when it is filled in and readable, the network's announcement
    /// otherwise.
    ///
    /// The code screen shows the address the phone holds as its own. It takes
    /// precedence over the announcement, which can point at another device:
    /// measured on two phones, where "adb mdns services" gave a single address
    /// to all of its instances.
    /// </summary>
    private (string Host, int Port) AddressToUse(PairingCandidateViewModel candidate)
    {
        if (AdbOutputParser.SplitNetworkSerial(PairingAddress.Trim()) is { Host: { } host, Port: { } port })
        {
            return (host, port);
        }

        return (candidate.Service.Host, candidate.Service.Port);
    }

    /// <summary>
    /// Connects to a manually entered port, with pairing already
    /// acquired.
    ///
    /// The host is not asked for: it is that of the phone we just
    /// paired with, and asking again would mean asking the user to
    /// find an address we already have.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (SelectedCandidate is not { } candidate
            || !int.TryParse(ConnectPort.Trim(), out var port))
        {
            return;
        }

        IsBusy = true;
        Status = Strings.Get("ConnectionInProgress");
        OnPropertyChanged(nameof(CanConnect));

        try
        {
            Settle(await _pairing
                .ConnectAsync(candidate.Service.Host, port, cancellationToken)
                .ConfigureAwait(true));
        }
        catch (AdbException exception)
        {
            Status = exception.UserMessage;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanConnect));
        }
    }

    /// <summary>
    /// Files away what an attempt says.
    ///
    /// The window only closes on a real connection. It used to close as
    /// soon as the phone had accepted the code, announcing "it will
    /// connect on its own" while it was not actually connected, and
    /// discarding the message that said what to do next. That message
    /// was asking for exactly the port that no field allowed entering:
    /// the user was stuck.
    /// </summary>
    private void Settle(WirelessPairingResult result)
    {
        NeedsPort = result.NeedsPort;

        // The address field stays once it is out: the user can mistype while
        // copying, and pulling it back on every attempt would take away what
        // was just handed to them.
        NeedsAddress |= result.NeedsAddress;

        Status = result.Connected
            ? Strings.Get("PhonePaired")
            : result.UserMessage;

        if (result.Connected)
        {
            DevicePaired?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnPairingCodeChanged(string value) => OnPropertyChanged(nameof(CanPair));

    partial void OnConnectPortChanged(string value) => OnPropertyChanged(nameof(CanConnect));

    partial void OnSelectedCandidateChanged(PairingCandidateViewModel? value)
    {
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanConnect));
    }
}

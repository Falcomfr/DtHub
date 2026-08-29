using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Adb;
using DtHub.Core.Devices;

namespace DtHub.App.ViewModels;

/// <summary>
/// Un appareil proposé dans la fenêtre d'ajout. Il vient soit des appareils
/// déjà connus d'ADB, soit d'une annonce réseau pour un téléphone que nous
/// n'avons encore jamais vu.
/// </summary>
public sealed partial class DeviceEntryViewModel : ObservableObject
{
    public DeviceEntryViewModel(string key, string name, string detail)
    {
        Key = key;
        _name = name;
        _detail = detail;
    }

    /// <summary>Identité stable de la ligne, pour la mettre à jour sans clignoter.</summary>
    public string Key { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _detail;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _status;

    /// <summary>Appareil mémorisé, quand la ligne en vient.</summary>
    public AndroidDevice? Device { get; init; }

    /// <summary>Annonce réseau, quand la ligne en vient.</summary>
    public MdnsService? Announcement { get; init; }

    public string StatusText => IsBusy ? "Connexion…" : IsConnected ? "Connecté" : Status ?? "Hors ligne";

    public bool CanConnect => !IsConnected && !IsBusy;

    public void Refresh()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanConnect));
    }

    partial void OnIsConnectedChanged(bool value) => Refresh();

    partial void OnIsBusyChanged(bool value) => Refresh();

    partial void OnStatusChanged(string? value) => Refresh();
}

/// <summary>
/// Fenêtre d'ajout d'un appareil. Elle liste tous les téléphones connus et
/// tout ce qui s'annonce sur le réseau, tente les connexions d'office, et ne
/// montre la marche à suivre que si on la demande.
/// </summary>
public sealed partial class AddDeviceViewModel : ObservableObject
{
    private readonly DevicePairingService _pairing;
    private readonly DeviceDiscoveryService _devices;
    private readonly DeviceReconnectService _reconnect;

    public AddDeviceViewModel(
        DevicePairingService pairing,
        DeviceDiscoveryService devices,
        DeviceReconnectService reconnect)
    {
        _pairing = pairing;
        _devices = devices;
        _reconnect = reconnect;
    }

    /// <summary>Tous les appareils : connus, connectés, ou vus sur le réseau.</summary>
    public ObservableCollection<DeviceEntryViewModel> Devices { get; } = [];

    /// <summary>Téléphones qui affichent un code d'association.</summary>
    public ObservableCollection<DeviceEntryViewModel> Pairable { get; } = [];

    /// <summary>Affichage de la marche à suivre détaillée.</summary>
    [ObservableProperty]
    private bool _showSteps;

    [ObservableProperty]
    private string _pairingCode = string.Empty;

    [ObservableProperty]
    private DeviceEntryViewModel? _selectedPairable;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Signalé dès qu'un appareil a été connecté.</summary>
    public event EventHandler? DeviceConnected;

    public bool CanPair => SelectedPairable is not null && PairingCode.Trim().Length > 0 && !IsBusy;

    /// <summary>
    /// Reconstruit la liste et tente de connecter d'office ce qui s'annonce.
    /// La tentative n'aboutit que pour les téléphones déjà associés à ce PC :
    /// ADB conserve la clé et refuse les autres.
    /// </summary>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(true);

            var connectedAddresses = discovery.Devices
                .Where(d => d.IsConnected)
                .Select(d => d.Serial)
                .ToHashSet(StringComparer.Ordinal);

            var opened = await _pairing.ConnectAnnouncedAsync(connectedAddresses, cancellationToken)
                .ConfigureAwait(true);

            if (opened.Count > 0)
            {
                DeviceConnected?.Invoke(this, EventArgs.Empty);

                // Relire tout de suite : ce qui vient d'être connecté doit
                // apparaître connecté, pas en attente.
                discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }

            var announced = await _pairing.FindConnectableAsync(cancellationToken).ConfigureAwait(true);

            SyncDevices(discovery.Devices, announced);
            SyncPairable(await _pairing.FindPairingCandidatesAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (AdbException exception)
        {
            Status = exception.UserMessage;
        }
    }

    /// <summary>Connecte un appareil de la liste, sans rien saisir.</summary>
    [RelayCommand]
    private async Task ConnectAsync(DeviceEntryViewModel? entry)
    {
        if (entry is null || entry.IsBusy)
        {
            return;
        }

        entry.IsBusy = true;
        entry.Status = null;

        try
        {
            var connected = entry switch
            {
                { Announcement: { } announcement } =>
                    (await _pairing.ConnectAsync(announcement.Host, announcement.Port).ConfigureAwait(true))
                        .Connected,

                { Device: { } device } =>
                    await _reconnect.TryReconnectAsync(device).ConfigureAwait(true)
                        is not ReconnectOutcome.NotFound,

                _ => false,
            };

            entry.IsConnected = connected;

            entry.Status = connected
                ? null
                : "Introuvable. Vérifiez qu'il est allumé, sur le même réseau, et que le débogage sans fil est actif.";

            if (connected)
            {
                DeviceConnected?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (AdbException exception)
        {
            entry.Status = exception.UserMessage;
        }
        finally
        {
            entry.IsBusy = false;
        }
    }

    /// <summary>Associe un téléphone qui affiche un code, puis le connecte.</summary>
    [RelayCommand]
    private async Task PairAsync(CancellationToken cancellationToken)
    {
        if (SelectedPairable?.Announcement is not { } target)
        {
            Status = "Choisissez le téléphone qui affiche un code d'association.";
            return;
        }

        var code = PairingCode.Trim();
        if (code.Length == 0)
        {
            Status = "Saisissez le code à six chiffres affiché sur le téléphone.";
            return;
        }

        IsBusy = true;
        Status = "Association en cours…";
        OnPropertyChanged(nameof(CanPair));

        try
        {
            var result = await _pairing
                .PairAndConnectAsync(target.Host, target.Port, code, cancellationToken)
                .ConfigureAwait(true);

            // Le code ne sert qu'une fois : il est effacé aussitôt.
            PairingCode = string.Empty;
            Status = result.UserMessage;

            if (result.Connected)
            {
                DeviceConnected?.Invoke(this, EventArgs.Empty);
                await ScanAsync(cancellationToken).ConfigureAwait(true);
            }
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

    [RelayCommand]
    private void ToggleSteps() => ShowSteps = !ShowSteps;

    /// <summary>
    /// Fusionne les appareils connus et les annonces réseau. Un téléphone
    /// cesse d'annoncer une fois connecté : se fier aux seules annonces
    /// viderait la liste précisément quand tout va bien.
    /// </summary>
    private void SyncDevices(IReadOnlyList<AndroidDevice> devices, IReadOnlyList<MdnsService> announced)
    {
        List<DeviceEntryViewModel> wanted = [];

        foreach (var device in devices)
        {
            wanted.Add(new DeviceEntryViewModel(
                device.Id,
                device.DisplayName,
                Describe(device))
            {
                Device = device,
                IsConnected = device.IsConnected,
            });
        }

        // Annonces qui ne correspondent à aucun appareil connu : un téléphone
        // associé sur un autre PC, ou remis à zéro ici.
        foreach (var service in announced)
        {
            if (devices.Any(d => service.MatchesSerial(d.Id) || d.Serial == service.Address))
            {
                continue;
            }

            wanted.Add(new DeviceEntryViewModel(service.Name, ShortName(service), service.Address)
            {
                Announcement = service,
            });
        }

        Merge(Devices, wanted);
    }

    private void SyncPairable(IReadOnlyList<MdnsService> candidates)
    {
        List<DeviceEntryViewModel> wanted =
        [
            .. candidates.Select(s => new DeviceEntryViewModel(s.Name, ShortName(s), s.Address)
            {
                Announcement = s,
            }),
        ];

        Merge(Pairable, wanted);

        // Un seul téléphone en attente : il est choisi d'office, il ne reste
        // alors que le code à saisir.
        if (SelectedPairable is null || !Pairable.Contains(SelectedPairable))
        {
            SelectedPairable = Pairable.FirstOrDefault();
        }
    }

    private static void Merge(
        ObservableCollection<DeviceEntryViewModel> target,
        IReadOnlyList<DeviceEntryViewModel> wanted)
    {
        foreach (var entry in wanted)
        {
            var existing = target.FirstOrDefault(e => string.Equals(e.Key, entry.Key, StringComparison.Ordinal));

            if (existing is null)
            {
                target.Add(entry);
                continue;
            }

            existing.Name = entry.Name;
            existing.Detail = entry.Detail;
            existing.IsConnected = entry.IsConnected;
        }

        foreach (var stale in target
            .Where(e => !wanted.Any(w => string.Equals(w.Key, e.Key, StringComparison.Ordinal)))
            .ToList())
        {
            target.Remove(stale);
        }
    }

    private static string Describe(AndroidDevice device) => device.State switch
    {
        AdbDeviceState.Device when device.ConnectionKind == AdbConnectionKind.Usb => "Branché en USB",
        AdbDeviceState.Device => device.ReconnectAddress ?? "Connecté en Wi-Fi",
        AdbDeviceState.Unauthorized => "À autoriser sur l'écran du téléphone",
        AdbDeviceState.NoPermissions => "Pilote USB refusé par Windows",
        _ => device.ReconnectAddress ?? "Déjà connu, actuellement éteint ou hors du réseau",
    };

    /// <summary>
    /// Nom lisible d'une annonce. Elle a la forme
    /// <c>adb-&lt;série&gt;-&lt;aléa&gt;</c> : le numéro de série suffit.
    /// </summary>
    private static string ShortName(MdnsService service)
    {
        var parts = service.Name.Split('-');
        return parts.Length >= 2 ? parts[1] : service.Name;
    }

    partial void OnPairingCodeChanged(string value) => OnPropertyChanged(nameof(CanPair));

    partial void OnSelectedPairableChanged(DeviceEntryViewModel? value) => OnPropertyChanged(nameof(CanPair));
}

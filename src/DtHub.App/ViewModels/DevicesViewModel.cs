using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Apps;
using DtHub.Core.Devices;

namespace DtHub.App.ViewModels;

/// <summary>
/// Page Appareils : liste, appairage Wi-Fi assisté, renommage, reconnexion,
/// oubli.
/// </summary>
public sealed partial class DevicesViewModel : PageViewModel
{
    private readonly DeviceDiscoveryService _discovery;
    private readonly DevicePairingService _pairing;
    private readonly DeviceReconnectService _reconnect;
    private readonly IDeviceRegistry _registry;
    private readonly AppDiscoveryService _apps;
    private readonly Core.Profiles.ProfileService _profiles;
    private readonly IDialogService _dialogs;

    public DevicesViewModel(
        DeviceDiscoveryService discovery,
        DevicePairingService pairing,
        DeviceReconnectService reconnect,
        IDeviceRegistry registry,
        AppDiscoveryService apps,
        Core.Profiles.ProfileService profiles,
        IDialogService dialogs)
    {
        _discovery = discovery;
        _pairing = pairing;
        _reconnect = reconnect;
        _registry = registry;
        _apps = apps;
        _profiles = profiles;
        _dialogs = dialogs;
    }

    public override string Title => "Appareils";

    public override string Subtitle =>
        "Les téléphones que DT Hub connaît. Branchez-en un en USB, ou associez-le en Wi-Fi.";

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    [ObservableProperty]
    private DeviceItemViewModel? _selectedDevice;

    // Assistant d'appairage Wi-Fi.

    [ObservableProperty]
    private bool _isPairingVisible;

    [ObservableProperty]
    private string _pairingAddress = string.Empty;

    [ObservableProperty]
    private string _pairingPort = string.Empty;

    [ObservableProperty]
    private string _pairingCode = string.Empty;

    [ObservableProperty]
    private string? _pairingStatus;

    /// <summary>Téléphones annonçant un appairage en cours sur le réseau.</summary>
    public ObservableCollection<Core.Adb.MdnsService> PairingCandidates { get; } = [];

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(RefreshCoreAsync, cancellationToken);

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken) =>
        RunAsync(RefreshCoreAsync, cancellationToken);

    [RelayCommand]
    private void StartPairing()
    {
        IsPairingVisible = true;
        PairingStatus = null;
        _ = LookForCandidatesAsync();
    }

    [RelayCommand]
    private void CancelPairing()
    {
        IsPairingVisible = false;

        // Le code d'appairage ne survit pas à la fermeture de l'assistant.
        PairingCode = string.Empty;
        PairingStatus = null;
    }

    [RelayCommand]
    private Task PairAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        var address = PairingAddress.Trim();
        var code = PairingCode.Trim();

        if (address.Length == 0 || code.Length == 0
            || !int.TryParse(PairingPort.Trim(), out var port) || port is < 1 or > 65535)
        {
            PairingStatus = "Renseignez l'adresse IP, le port d'association et le code affichés sur le téléphone.";
            return;
        }

        PairingStatus = "Association en cours…";

        var result = await _pairing.PairAndConnectAsync(address, port, code, token).ConfigureAwait(true);

        // Le code ne sert qu'une fois : il est effacé aussitôt.
        PairingCode = string.Empty;
        PairingStatus = result.UserMessage;

        if (result.Connected)
        {
            IsPairingVisible = false;
            await RefreshCoreAsync(token).ConfigureAwait(true);
        }
    }, cancellationToken);

    /// <summary>Connexion directe, quand le mDNS est bloqué et que l'utilisateur saisit le port.</summary>
    [RelayCommand]
    private Task ConnectDirectlyAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        var address = PairingAddress.Trim();

        if (address.Length == 0 || !int.TryParse(PairingPort.Trim(), out var port))
        {
            PairingStatus = "Renseignez l'adresse IP et le port de connexion affichés sur le téléphone.";
            return;
        }

        var result = await _pairing.ConnectAsync(address, port, token).ConfigureAwait(true);
        PairingStatus = result.UserMessage;

        if (result.Connected)
        {
            IsPairingVisible = false;
            await RefreshCoreAsync(token).ConfigureAwait(true);
        }
    }, cancellationToken);

    [RelayCommand]
    private Task ReconnectAsync(DeviceItemViewModel? item) => RunAsync(async token =>
    {
        if (item is null)
        {
            return;
        }

        var outcome = await _reconnect.TryReconnectAsync(item.Device, token).ConfigureAwait(true);

        StatusMessage = outcome switch
        {
            ReconnectOutcome.AlreadyConnected => $"{item.DisplayName} est déjà connecté.",
            ReconnectOutcome.NotFound =>
                $"{item.DisplayName} reste introuvable. Vérifiez qu'il est allumé, sur le même réseau, "
                + "et que le débogage sans fil est actif.",
            _ => $"{item.DisplayName} est reconnecté.",
        };

        await RefreshCoreAsync(token).ConfigureAwait(true);
    });

    [RelayCommand]
    private Task RenameAsync(DeviceItemViewModel? item) => RunAsync(async token =>
    {
        if (item is null)
        {
            return;
        }

        var name = RenameRequested?.Invoke(item.DisplayName);
        if (name is null)
        {
            return;
        }

        await _registry.RenameAsync(item.Id, name, token).ConfigureAwait(true);
        await RefreshCoreAsync(token).ConfigureAwait(true);
    });

    [RelayCommand]
    private Task SetPrimaryAsync(DeviceItemViewModel? item) => RunAsync(async token =>
    {
        if (item is null)
        {
            return;
        }

        await _registry.SetPrimaryAsync(item.Id, token).ConfigureAwait(true);
        await RefreshCoreAsync(token).ConfigureAwait(true);
    });

    [RelayCommand]
    private Task ForgetAsync(DeviceItemViewModel? item) => RunAsync(async token =>
    {
        if (item is null
            || !_dialogs.Confirm(
                $"Oublier {item.DisplayName} ?\n\nSes sessions seront retirées de tous les profils.",
                "Oublier l'appareil"))
        {
            return;
        }

        await _registry.ForgetAsync(item.Id, token).ConfigureAwait(true);
        await _apps.ForgetAsync(item.Id, token).ConfigureAwait(true);
        await _profiles.RemoveDeviceEverywhereAsync(item.Id, token).ConfigureAwait(true);

        await RefreshCoreAsync(token).ConfigureAwait(true);
    });

    /// <summary>
    /// Demande de saisie du nouveau nom. Branché par la vue, pour que la
    /// vue-modèle n'ouvre pas elle-même de fenêtre.
    /// </summary>
    public Func<string, string?>? RenameRequested { get; set; }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var result = await _discovery.RefreshAsync(cancellationToken).ConfigureAwait(true);
        var selectedId = SelectedDevice?.Id;

        Devices.Clear();
        foreach (var device in result.Devices)
        {
            Devices.Add(new DeviceItemViewModel(device));
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selectedId) ?? Devices.FirstOrDefault();

        if (result.Warnings.Count > 0)
        {
            StatusMessage = string.Join(" ", result.Warnings);
        }
    }

    /// <summary>
    /// Cherche les téléphones qui affichent un code d'association, pour
    /// pré-remplir l'adresse et le port plutôt que de les faire recopier.
    /// </summary>
    private async Task LookForCandidatesAsync()
    {
        try
        {
            var candidates = await _pairing.FindPairingCandidatesAsync().ConfigureAwait(true);

            PairingCandidates.Clear();
            foreach (var candidate in candidates)
            {
                PairingCandidates.Add(candidate);
            }

            if (PairingCandidates.Count == 1 && PairingAddress.Length == 0)
            {
                PairingAddress = PairingCandidates[0].Host;
                PairingPort = PairingCandidates[0].Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (Core.Adb.AdbException)
        {
            // La découverte est un confort : son échec ne bloque pas la saisie
            // manuelle.
        }
    }
}

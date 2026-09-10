using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>Un téléphone qui affiche un code d'association.</summary>
public sealed partial class PairingCandidateViewModel : ObservableObject
{
    public PairingCandidateViewModel(MdnsService service) => Service = service;

    public MdnsService Service { get; }

    public string Address => Service.Address;

    /// <summary>
    /// Nom lisible. L'annonce a la forme <c>adb-&lt;série&gt;-&lt;aléa&gt;</c> :
    /// le numéro de série suffit à distinguer deux téléphones.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var parts = Service.Name.Split('-');
            return parts.Length >= 2 ? parts[1] : Service.Name;
        }
    }
}

/// <summary>
/// Fenêtre d'association d'un téléphone neuf. Elle ne sert qu'à cela : la
/// connexion des téléphones déjà associés se fait toute seule, dans la fenêtre
/// principale.
/// </summary>
public sealed partial class AddDeviceViewModel : ObservableObject
{
    private readonly DevicePairingService _pairing;
    private readonly IDeviceRegistry _registry;

    /// <summary>
    /// Hôte du téléphone qui vient d'accepter le code, tant qu'il n'est pas
    /// encore connecté.
    ///
    /// Le code accepté ne suffit pas à jouer : il faut encore que le téléphone
    /// s'annonce sur le réseau et qu'on s'y connecte. Cette annonce arrive
    /// souvent après que la tentative a rendu la main, et la fenêtre restait
    /// alors ouverte pour toujours sur un appairage pourtant réussi. Relevé sur
    /// le poste, deux appairages coup sur coup faute de voir le premier
    /// aboutir.
    /// </summary>
    private string? _awaitedHost;

    public AddDeviceViewModel(DevicePairingService pairing, IDeviceRegistry registry)
    {
        _pairing = pairing;
        _registry = registry;
    }

    /// <summary>Téléphones qui affichent un code d'association.</summary>
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
    /// Vrai quand l'appairage a pris mais que le port de connexion reste à
    /// saisir. C'est alors, et alors seulement, que le champ apparaît.
    /// </summary>
    [ObservableProperty]
    private bool _needsPort;

    /// <summary>Le port que l'utilisateur lit sur son téléphone.</summary>
    [ObservableProperty]
    private string _connectPort = string.Empty;

    /// <summary>Signalé après une association réussie.</summary>
    public event EventHandler? DevicePaired;

    public bool CanPair => SelectedCandidate is not null && PairingCode.Trim().Length > 0 && !IsBusy;

    public bool CanConnect =>
        SelectedCandidate is not null
        && !IsBusy
        && int.TryParse(ConnectPort.Trim(), out var port)
        && port is > 0 and <= 65535;

    /// <summary>
    /// Cherche les téléphones qui affichent un code. Appelée en boucle : le
    /// téléphone apparaît dès que l'écran d'association est ouvert.
    /// </summary>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Le téléphone appairé s'est-il annoncé depuis ? Ce balayage tourne
            // déjà toutes les deux secondes : il n'en coûte rien de le lui
            // demander, et c'est ce qui manquait pour refermer la fenêtre.
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
                if (!Candidates.Any(c => string.Equals(c.Service.Name, service.Name, StringComparison.Ordinal)))
                {
                    Candidates.Add(new PairingCandidateViewModel(service));
                }
            }

            foreach (var stale in Candidates
                .Where(c => !found.Any(s => string.Equals(s.Name, c.Service.Name, StringComparison.Ordinal)))
                .ToList())
            {
                Candidates.Remove(stale);
            }

            // Un seul téléphone en attente : il est choisi d'office, il ne
            // reste alors que le code à saisir.
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
    /// Vrai si le téléphone attendu s'annonce enfin et accepte la connexion.
    ///
    /// L'hôte est comparé, et non pas simplement « quelque chose s'est
    /// connecté » : un autre téléphone déjà associé peut s'annoncer au même
    /// moment, et refermer la fenêtre sur son dos donnerait à croire que
    /// l'association vient d'aboutir.
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
            var result = await _pairing
                .PairAndConnectAsync(candidate.Service.Host, candidate.Service.Port, code, cancellationToken)
                .ConfigureAwait(true);

            // Le code ne sert qu'une fois : il est effacé aussitôt.
            PairingCode = string.Empty;

            // Une association acceptée lève l'écart, et elle seule : c'est le
            // geste explicite par lequel on revient sur une rupture. Sans cela,
            // un appareil écarté resterait refusé alors qu'on vient de retaper
            // son code, et rien ne le dirait.
            if (result.Paired
                && MdnsDeviceName.HardwareSerialFromInstance(candidate.Service.Name) is { Length: > 0 } serial)
            {
                await _registry.WelcomeBackAsync(serial, cancellationToken).ConfigureAwait(true);
            }

            Settle(result);

            // Le code est accepté mais la connexion n'est pas venue : on guette
            // l'annonce du téléphone au lieu de laisser la fenêtre ouverte.
            _awaitedHost = result.Paired && !result.Connected ? candidate.Service.Host : null;
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
    /// Connecte à un port saisi à la main, l'appairage étant déjà acquis.
    ///
    /// L'hôte n'est pas demandé : c'est celui du téléphone qu'on vient
    /// d'appairer, et le redemander serait demander à l'utilisateur de retrouver
    /// une adresse que nous avons déjà.
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
        Status = "Connexion en cours…";
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
    /// Range ce que dit une tentative.
    ///
    /// La fenêtre ne se ferme que sur une vraie connexion. Elle se fermait dès
    /// que le téléphone avait accepté le code, en annonçant « il se connectera
    /// tout seul » alors qu'il n'était pas connecté, et en jetant le message qui
    /// disait quoi faire. Ce message demandait justement un port qu'aucun champ
    /// ne permettait de saisir : l'utilisateur était dans une impasse.
    /// </summary>
    private void Settle(WirelessPairingResult result)
    {
        NeedsPort = result.NeedsPort;

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

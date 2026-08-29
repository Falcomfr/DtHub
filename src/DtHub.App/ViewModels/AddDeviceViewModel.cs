using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;

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
    private readonly DeviceDiscoveryService _devices;

    public AddDeviceViewModel(DevicePairingService pairing, DeviceDiscoveryService devices)
    {
        _pairing = pairing;
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>Marques proposées, pour adapter les chemins de menu.</summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

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

    /// <summary>Signalé après une association réussie.</summary>
    public event EventHandler? DevicePaired;

    public bool CanPair => SelectedCandidate is not null && PairingCode.Trim().Length > 0 && !IsBusy;

    public bool HasWarning => !string.IsNullOrWhiteSpace(Brand.Warning);

    /// <summary>
    /// Présélectionne la marque du téléphone déjà connu, quand il y en a un :
    /// on ajoute souvent un second téléphone de la même marque.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(true);

            var manufacturer = discovery.Devices
                .Select(d => d.Manufacturer)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

            if (manufacturer is not null)
            {
                Brand = PhoneBrands.FromManufacturer(manufacturer);
            }
        }
        catch (AdbException)
        {
            // Sans appareil connu, la marque par défaut convient.
        }
    }

    /// <summary>
    /// Cherche les téléphones qui affichent un code. Appelée en boucle : le
    /// téléphone apparaît dès que l'écran d'association est ouvert.
    /// </summary>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
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

    [RelayCommand]
    private async Task PairAsync(CancellationToken cancellationToken)
    {
        if (SelectedCandidate is not { } candidate)
        {
            Status = "Aucun téléphone n'affiche de code d'association pour l'instant.";
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
                .PairAndConnectAsync(candidate.Service.Host, candidate.Service.Port, code, cancellationToken)
                .ConfigureAwait(true);

            // Le code ne sert qu'une fois : il est effacé aussitôt.
            PairingCode = string.Empty;
            Status = result.Paired
                ? "Téléphone associé. Il se connectera tout seul, maintenant et à chaque lancement."
                : result.UserMessage;

            if (result.Paired)
            {
                DevicePaired?.Invoke(this, EventArgs.Empty);
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
    private void SelectBrand(PhoneBrand? brand)
    {
        if (brand is not null)
        {
            Brand = brand;
        }
    }

    partial void OnBrandChanged(PhoneBrand value) => OnPropertyChanged(nameof(HasWarning));

    partial void OnPairingCodeChanged(string value) => OnPropertyChanged(nameof(CanPair));

    partial void OnSelectedCandidateChanged(PairingCandidateViewModel? value) =>
        OnPropertyChanged(nameof(CanPair));
}

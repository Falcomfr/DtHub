using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>
/// Que faire quand l'image s'affiche mais que rien ne répond.
///
/// C'est le symptôme le plus fréquent du terrain, et le plus démuni : scrcpy
/// n'échoue pas, aucun message n'apparaît, le clic ne fait simplement rien.
/// Relevé sur le salon d'entraide d'un produit concurrent qui emprunte le même
/// chemin, une dizaine de personnes le posent sur plusieurs semaines, et la
/// réponse circule de bouche à oreille sans jamais être écrite nulle part.
///
/// La cause tient à un réglage dont le sous-titre dit tout : « Accorder les
/// autorisations et simulation d'entrée via le débogage USB ». Sans lui, ADB
/// affiche mais n'injecte pas.
///
/// La fiche nommait la cause sans savoir la constater. Elle sait maintenant
/// poser la question à l'appareil, sur demande, et rendre l'un des trois seuls
/// verdicts qu'elle peut tenir.
/// </summary>
public sealed partial class InputHelpViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService _devices;

    public InputHelpViewModel(DeviceDiscoveryService devices)
    {
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>Marques proposées, regroupées quand la procédure est la même.</summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    /// <summary>Vrai quand la marque ajoute un piège qui lui est propre.</summary>
    public bool HasWarning => !string.IsNullOrWhiteSpace(Brand.Warning);

    /// <summary>Numéro de série de l'appareil interrogé, vide s'il n'y en a pas.</summary>
    private string _serial = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _verdict = string.Empty;

    /// <summary>Vrai quand un verdict est là et mérite d'être montré.</summary>
    public bool HasVerdict => !string.IsNullOrWhiteSpace(Verdict);

    /// <summary>Vrai quand le verdict est celui qui désigne la cause.</summary>
    [ObservableProperty]
    private bool _verdictIsRefusal;

    /// <summary>Présélectionne la marque de l'appareil branché.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(true);

            var connected = discovery.Devices.FirstOrDefault(d => d.IsConnected);

            _serial = connected?.Serial ?? string.Empty;

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
            // Sans appareil joignable, la procédure standard fait l'affaire.
        }
    }

    /// <summary>
    /// Demande à l'appareil s'il accepte la simulation d'entrée.
    ///
    /// L'appareil est relu à chaque test : entre l'ouverture de la fiche et le
    /// clic, on a pu brancher le câble ou changer le réglage, et c'est
    /// justement ce qu'on vient vérifier.
    /// </summary>
    [RelayCommand]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        IsTesting = true;
        Verdict = string.Empty;
        VerdictIsRefusal = false;
        OnPropertyChanged(nameof(HasVerdict));

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(true);

            if (string.IsNullOrEmpty(_serial))
            {
                Verdict = Strings.Get("DeadInputTestNoDevice");
                return;
            }

            var answer = await _devices
                .CheckInputInjectionAsync(_serial, cancellationToken)
                .ConfigureAwait(true);

            VerdictIsRefusal = answer == InputInjection.Denied;

            Verdict = Strings.Get(answer switch
            {
                InputInjection.Works => "DeadInputTestWorks",
                InputInjection.Denied => "DeadInputTestDenied",
                _ => "DeadInputTestUnknown",
            });
        }
        finally
        {
            IsTesting = false;
            OnPropertyChanged(nameof(HasVerdict));
        }
    }

    partial void OnBrandChanged(PhoneBrand value) => OnPropertyChanged(nameof(HasWarning));
}

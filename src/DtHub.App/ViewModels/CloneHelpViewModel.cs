using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;

namespace DtHub.App.ViewModels;

/// <summary>
/// Comment obtenir une seconde installation du jeu sur un même appareil.
/// La marche à suivre dépend de la surcouche : chaque constructeur a nommé
/// la fonction autrement et l'a rangée ailleurs.
/// </summary>
public sealed partial class CloneHelpViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService _devices;

    public CloneHelpViewModel(DeviceDiscoveryService devices)
    {
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>Marques proposées, regroupées quand la procédure est la même.</summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    public bool HasNote => !string.IsNullOrWhiteSpace(Brand.CloneNote);

    /// <summary>Présélectionne la marque de l'appareil branché.</summary>
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
            // Sans appareil joignable, la procédure standard fait l'affaire.
        }
    }

    partial void OnBrandChanged(PhoneBrand value) => OnPropertyChanged(nameof(HasNote));
}

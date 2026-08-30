using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;

namespace DtHub.App.ViewModels;

/// <summary>
/// Comment empêcher Android d'endormir le jeu. Comme pour la duplication, le
/// réglage existe partout mais chaque constructeur l'a nommé autrement et
/// rangé ailleurs, et plusieurs en ajoutent un second.
/// </summary>
public sealed partial class SleepHelpViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService _devices;

    public SleepHelpViewModel(DeviceDiscoveryService devices)
    {
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>Marques proposées, regroupées quand la procédure est la même.</summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    public bool HasNote => !string.IsNullOrWhiteSpace(Brand.BatteryNote);

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

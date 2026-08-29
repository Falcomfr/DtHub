using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;

namespace DtHub.App.ViewModels;

/// <summary>
/// Fenêtre d'aide : la marche à suivre sur le téléphone, adaptée à sa marque.
/// Séparée de l'association pour que celle-ci reste courte.
/// </summary>
public sealed partial class HelpViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService _devices;

    public HelpViewModel(DeviceDiscoveryService devices)
    {
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>Marques proposées, regroupées par procédure identique.</summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    public bool HasWarning => !string.IsNullOrWhiteSpace(Brand.Warning);

    /// <summary>
    /// Présélectionne la marque d'un téléphone déjà connu : on ajoute souvent
    /// un second appareil de la même marque.
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
            // Sans appareil connu, la procédure standard convient.
        }
    }

    partial void OnBrandChanged(PhoneBrand value) => OnPropertyChanged(nameof(HasWarning));
}

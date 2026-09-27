using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;

namespace DtHub.App.ViewModels;

/// <summary>
/// Help window: the steps to follow on the phone, adapted to its
/// brand. Kept separate from pairing so that it stays short.
/// </summary>
public sealed partial class HelpViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService _devices;

    public HelpViewModel(DeviceDiscoveryService devices)
    {
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>Brands offered, grouped by identical procedure.</summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    public bool HasWarning => !string.IsNullOrWhiteSpace(Brand.Warning);

    /// <summary>
    /// The maker of the phone this help was opened for, when it was
    /// opened from one rather than from the pairing window.
    /// </summary>
    public string? OpenedFor { get; set; }

    /// <summary>
    /// Preselects the brand of an already known phone: a second
    /// device of the same brand is often added.
    ///
    /// When the help was opened from one phone in particular, that one
    /// wins and nothing is discovered: it is the phone whose steps are
    /// wanted, and it is precisely the one that is not answering, so
    /// asking the others would name a brand that is not its own.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(OpenedFor))
        {
            Brand = PhoneBrands.FromManufacturer(OpenedFor);
            return;
        }

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
            // Without a known device, the standard procedure will do.
        }
    }

    partial void OnBrandChanged(PhoneBrand value) => OnPropertyChanged(nameof(HasWarning));
}

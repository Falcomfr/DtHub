using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Guidance;

namespace DtHub.App.ViewModels;

/// <summary>
/// How to prevent Android from putting the game to sleep. As with
/// app duplication, the setting exists everywhere but each
/// manufacturer has named it differently and put it somewhere else,
/// and several add a second one.
/// </summary>
public sealed partial class SleepHelpViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService _devices;

    public SleepHelpViewModel(DeviceDiscoveryService devices)
    {
        _devices = devices;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>
    /// Brands offered, grouped together when the procedure is the
    /// same.
    /// </summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    public bool HasNote => !string.IsNullOrWhiteSpace(Brand.BatteryNote);

    /// <summary>Preselects the brand of the connected device.</summary>
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
            // Without a reachable device, the standard procedure will do.
        }
    }

    partial void OnBrandChanged(PhoneBrand value)
    {
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(BatteryPath));
    }

    /// <summary>
    /// The app whose battery settings to change: the game by default, a
    /// DOFUS Touch tool when its alert line opened this help.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryPath))]
    private string _app = DofusPackages.GameName;

    /// <summary>
    /// The brand's path, with the app's name in place of the game's: every
    /// brand's path names the game the same way, in every language.
    /// </summary>
    public string BatteryPath => Brand.BatteryPath.Replace(DofusPackages.GameName, App, StringComparison.Ordinal);
}

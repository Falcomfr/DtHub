using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Guidance;
using DtHub.Core.Localization;
using DtHub.Core.Settings;

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
    private readonly SettingsService _settings;

    public SleepHelpViewModel(DeviceDiscoveryService devices, SettingsService settings)
    {
        _devices = devices;
        _settings = settings;
        _brand = PhoneBrands.Standard;
    }

    /// <summary>
    /// The phone whose alert opened this help, or <c>null</c> from the
    /// general help. Only then can its alert be hidden: the button sits
    /// here, under the steps, so it is never pressed without seeing them.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMute))]
    private string? _deviceId;

    public bool CanMute => DeviceId is not null;

    public string MuteNote => Strings.Format("MuteBatteryAlertNote", App);

    /// <summary>True when an alert was hidden somewhere, so it can be given back.</summary>
    [ObservableProperty]
    private bool _hasMuted;

    public async Task MuteAsync()
    {
        if (DeviceId is not null)
        {
            _ = await _settings.MuteBatteryAlertAsync(DeviceId, App).ConfigureAwait(true);
        }
    }

    public async Task UnmuteAllAsync()
    {
        _ = await _settings.UnmuteBatteryAlertsAsync().ConfigureAwait(true);
        HasMuted = false;
    }

    /// <summary>
    /// Brands offered, grouped together when the procedure is the
    /// same.
    /// </summary>
    public IReadOnlyList<PhoneBrand> Brands { get; } = PhoneBrands.All;

    [ObservableProperty]
    private PhoneBrand _brand;

    public bool HasNote => !string.IsNullOrWhiteSpace(Brand.BatteryNote);

    /// <summary>
    /// Preselects the brand of the phone the alert is about, or else of
    /// the first connected one: a Galaxy Tab beside a Xiaomi used to get
    /// the Xiaomi path.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        HasMuted = (await _settings.GetAsync(cancellationToken).ConfigureAwait(true)).MutedBatteryAlerts.Count > 0;

        try
        {
            var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(true);

            var manufacturer = discovery.Devices
                .OrderByDescending(d => string.Equals(d.Id, DeviceId, StringComparison.Ordinal))
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
    [NotifyPropertyChangedFor(nameof(BatteryPath), nameof(IsTool), nameof(Heading), nameof(Explanation),
        nameof(OpenStep), nameof(RemoveHelp), nameof(FrozenHelp), nameof(MuteNote))]
    private string _app = DofusPackages.GameName;

    /// <summary>A DOFUS Touch tool rather than the game: the texts name it.</summary>
    public bool IsTool => !string.Equals(App, DofusPackages.GameName, StringComparison.Ordinal);

    public string Heading => IsTool ? Strings.Format("ToolWindowFreezes", App) : Strings.Get("WindowFreezes");

    public string Explanation => IsTool ? Strings.Format("ToolSleepExplanation", App) : Strings.Get("SleepExplanation");

    public string OpenStep => IsTool ? Strings.Format("ToolOpenBatterySetting", App) : Strings.Get("OpenBatterySetting");

    public string RemoveHelp => IsTool ? Strings.Format("ToolRemoveRestrictionsHelp", App) : Strings.Get("RemoveRestrictionsHelp");

    public string FrozenHelp => IsTool ? Strings.Format("ToolAlreadyFrozenHelp", App) : Strings.Get("AlreadyFrozenHelp");

    /// <summary>
    /// The brand's path, with the app's name in place of the game's: every
    /// brand's path names the game the same way, in every language.
    /// </summary>
    public string BatteryPath => Brand.BatteryPath.Replace(DofusPackages.GameName, App, StringComparison.Ordinal);
}

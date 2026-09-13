using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>
/// A phone, as it appears above its instances.
///
/// The instances no longer belong to it: they live in a single list
/// where they sort themselves freely. This object is shared by every
/// row of the same phone, so a single state update notifies all of
/// them.
/// </summary>
public sealed partial class DeviceGroupViewModel : ObservableObject
{
    public DeviceGroupViewModel(string deviceId, string name) => (DeviceId, _name) = (deviceId, name);

    public string DeviceId { get; }

    [ObservableProperty]
    private string _name;

    /// <summary>
    /// ADB serial number, which is an address over Wi-Fi and therefore
    /// changes. The stable identity is <see cref="DeviceId"/>; this one
    /// is only used to address a command.
    /// </summary>
    public string Serial { get; private set; } = string.Empty;

    /// <summary>
    /// True while an account is being added. Creating a profile,
    /// installing the game in it and starting it takes the phone about
    /// fifteen seconds: without this marker, the button stayed
    /// clickable and nothing said that it was working.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private AdbDeviceState _state = AdbDeviceState.Offline;

    [ObservableProperty]
    private AdbConnectionKind _connection = AdbConnectionKind.Unknown;

    public bool IsConnected => State == AdbDeviceState.Device;

    /// <summary>
    /// True when the device answers but no profile carries the game.
    /// It then has no row in the list, and would disappear without a
    /// word.
    /// </summary>
    private bool _hasNoGame;

    /// <summary>
    /// True while the phone is there but its accounts have not been looked for
    /// yet.
    ///
    /// Finding them asks each profile of each phone two questions and was
    /// measured at 2.9 seconds, so the phones are shown before it runs. During
    /// that gap a phone that has the game is indistinguishable from one that
    /// does not, and saying the game is missing would be a plain lie: this
    /// state is what keeps the window from telling it.
    /// </summary>
    private bool _lookingForGames;

    /// <summary>See <see cref="_lookingForGames" />.</summary>
    public bool IsLookingForGames
    {
        get => _lookingForGames && IsConnected;
        set
        {
            if (_lookingForGames == value)
            {
                return;
            }

            _lookingForGames = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNoGame));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrushKey));
        }
    }

    /// <summary>
    /// True only for a reachable device without the game.
    ///
    /// Never true while the accounts are still being looked for: the absence
    /// of a row means nothing yet at that point.
    /// </summary>
    public bool HasNoGame
    {
        get => _hasNoGame && IsConnected && !IsLookingForGames;
        set
        {
            if (_hasNoGame == value)
            {
                return;
            }

            _hasNoGame = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrushKey));
        }
    }

    /// <summary>
    /// True when this device announces itself on the network and
    /// refuses this PC.
    ///
    /// This is the one case where "Offline" is misleading: the phone
    /// is there, powered on, its wireless debugging is active, and all
    /// that is missing is a new pairing. Without this word, one looks
    /// toward the network, turns back on what is already on, and has
    /// no reason to think of pairing again since nothing was ever
    /// unpaired.
    /// </summary>
    private bool _needsPairing;

    /// <summary>
    /// True when pairing has to be redone, and only then.
    ///
    /// The resulting indication fits on a short line, under the one
    /// device concerned: a sentence under every offline device would
    /// take over the list's space to repeat something that is only
    /// true one time in four.
    /// </summary>
    public bool NeedsPairing => _needsPairing && !IsConnected;

    /// <summary>Sets the doubt on pairing, and notifies the display.</summary>
    public void SetNeedsPairing(bool needed)
    {
        if (_needsPairing == needed)
        {
            return;
        }

        _needsPairing = needed;

        OnPropertyChanged(nameof(NeedsPairing));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTip));
        OnPropertyChanged(nameof(StatusBrushKey));
    }

    public string StatusText => _needsPairing && !IsConnected
        ? Strings.Get("ToPairAgain")
        : IsLookingForGames
        ? Strings.Get("LookingForGames")
        : HasNoGame
        ? Strings.Get("GameNotInstalled")
        : State switch
        {
            AdbDeviceState.Device => Strings.Get(
                Connection == AdbConnectionKind.Usb ? "ConnectedByUsb" : "ConnectedByWifi"),
            AdbDeviceState.Unauthorized => Strings.Get("ToAuthorizeOnPhone"),
            AdbDeviceState.Offline => Strings.Get("Offline"),
            AdbDeviceState.NoPermissions => Strings.Get("UsbDriverRefused"),
            _ => Strings.Get("UnknownState"),
        };

    /// <summary>
    /// What the hover tip explains, when the state alone is not enough.
    ///
    /// Two words in a list cannot say what to do. "To pair again" says
    /// what is missing, and the tooltip says how.
    ///
    /// **"Offline" deserves its own, and that is the case that was
    /// missing.** The application does not always know why a phone is
    /// not answering: it may be off, on another network, have its
    /// wireless debugging turned off, or have forgotten this PC's key.
    /// It cannot decide, but it can say in what order to look, instead
    /// of leaving it to guesswork. A user spent an evening on exactly
    /// this question, and the answer was the last one on the list.
    /// </summary>
    public string? StatusTip => !IsConnected
        ? Strings.Get(_needsPairing ? "ToPairAgainTip" : "OfflineTip")
        : null;

    public string StatusBrushKey => _needsPairing && !IsConnected
        ? "WarningBrush"
        // Work in progress is not a warning: it stays muted, so a phone that
        // turns out to hold the game never flashed orange on the way.
        : IsLookingForGames ? "TextMutedBrush"
        : HasNoGame ? "WarningBrush" : State switch
        {
            AdbDeviceState.Device => "SuccessBrush",
            AdbDeviceState.Unauthorized => "WarningBrush",
            AdbDeviceState.NoPermissions => "DangerBrush",
            _ => "TextMutedBrush",
        };

    /// <summary>
    /// The last battery reading, or <c>null</c> until it is known.
    ///
    /// It was already taken every minute, and only the twenty percent
    /// alert came out of it. The level itself is worth more: it is
    /// checked before launching five accounts, not once it is too
    /// late.
    /// </summary>
    private BatteryReading? _battery;

    /// <summary>Inner width of the gauge, in layout pixels.</summary>
    private const double GaugeWidth = 14;

    /// <summary>True when there is a level to show.</summary>
    public bool HasBattery => _battery is not null && IsConnected;

    /// <summary>The level alone, "84 %".</summary>
    public string BatteryText => _battery?.Label ?? string.Empty;

    /// <summary>
    /// The level as a sentence, with charging status if relevant.
    /// </summary>
    public string BatterySummary => _battery?.Summary ?? string.Empty;

    /// <summary>
    /// True when the device is charging, which is what the lightning
    /// bolt says.
    /// </summary>
    public bool IsCharging => _battery?.Charging ?? false;

    /// <summary>
    /// The filled share of the gauge. Never quite zero: at three
    /// percent, an empty-looking gauge reads as a broken one.
    /// </summary>
    public double BatteryFill => _battery is null
        ? 0
        : Math.Max(2, Math.Round(GaugeWidth * _battery.Percent / 100.0));

    /// <summary>
    /// The color of the fill. Discreet as long as nothing is urgent: a
    /// gauge that screams at eighty percent teaches nothing.
    /// </summary>
    public string BatteryBrushKey => _battery?.Concern switch
    {
        HealthSeverity.Serious => "DangerBrush",
        HealthSeverity.Warning => "WarningBrush",
        _ => "TextMutedBrush",
    };

    /// <summary>
    /// What is wrong with this device, one finding per line, or
    /// <c>null</c> when it has nothing.
    ///
    /// Under its own name rather than in a shared banner: gathered at
    /// the bottom of the list, the findings seemed to be talking about
    /// the last device shown.
    /// </summary>
    private string? _problems;

    private bool _problemsAreSerious;

    /// <summary>True when this device has something to report.</summary>
    public bool HasProblems => !string.IsNullOrEmpty(_problems);

    /// <summary>
    /// The findings, one per line, from the most serious to the most
    /// trivial.
    /// </summary>
    public string Problems => _problems ?? string.Empty;

    /// <summary>
    /// The color of the icon and the text: red if the session is at
    /// stake.
    /// </summary>
    public string ProblemsBrushKey => _problemsAreSerious ? "DangerBrush" : "WarningBrush";

    /// <summary>Sets what the health check found for this device.</summary>
    public void SetProblems(string? problems, bool serious)
    {
        if (string.Equals(_problems, problems, StringComparison.Ordinal)
            && _problemsAreSerious == serious)
        {
            return;
        }

        (_problems, _problemsAreSerious) = (problems, serious);

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(ProblemsBrushKey));
    }

    /// <summary>Sets the last reading, and notifies the display.</summary>
    public void SetBattery(BatteryReading? battery)
    {
        if (_battery == battery)
        {
            return;
        }

        _battery = battery;

        OnPropertyChanged(nameof(HasBattery));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(BatterySummary));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(BatteryFill));
        OnPropertyChanged(nameof(BatteryBrushKey));
    }

    public void Update(AndroidDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Name = device.DisplayName;
        Serial = device.Serial;
        State = device.State;
        Connection = device.ConnectionKind;

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasNoGame));
        OnPropertyChanged(nameof(IsLookingForGames));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
        OnPropertyChanged(nameof(HasBattery));
    }

}

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
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
    /// What this phone is known to carry.
    ///
    /// **One value where there used to be two booleans, and that is the
    /// fix.** The claim "no game" and the cosmetic "still looking"
    /// shared nothing but an <c>&amp;&amp;</c> in the display, and they
    /// were written from two different places. Lowering the second for
    /// a purely cosmetic reason silently removed the only guard on the
    /// first, and both phones started announcing "Game not installed"
    /// several times a minute. Three values cannot contradict each
    /// other, and "not known yet" stops being spellable as "absent".
    /// </summary>
    public GamePresence Presence { get; private set; }

    /// <summary>Records what the last pass established, and nothing more.</summary>
    public void SetPresence(GamePresence presence)
    {
        if (Presence == presence)
        {
            return;
        }

        Presence = presence;

        OnPropertyChanged(nameof(Presence));
        OnPropertyChanged(nameof(IsLookingForGames));
        OnPropertyChanged(nameof(HasNoGame));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
    }

    /// <summary>
    /// True while the phone is there and its accounts are not
    /// established yet.
    ///
    /// Finding them asks each profile of each phone two questions and
    /// was measured at 2.9 seconds, so the phones are shown before it
    /// runs. During that gap a phone that has the game is
    /// indistinguishable from one that does not.
    /// </summary>
    public bool IsLookingForGames => Presence == GamePresence.Unknown && IsConnected;

    /// <summary>
    /// True only for a reachable device the search answered about, and
    /// answered that no profile carries the game. It then has no row in
    /// the list, and would disappear without a word.
    /// </summary>
    public bool HasNoGame => Presence == GamePresence.Absent && IsConnected;

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

    /// <summary>
    /// The four readings taken of this device, or <c>null</c> before it
    /// has been asked anything.
    /// </summary>
    private DeviceVitals? _vitals;

    /// <summary>
    /// True when the device is hot enough for it to be worth a mark.
    ///
    /// Same rule as the free space beside it: a reading that is always
    /// comfortable teaches nothing and occupies a place. "Cool" was true
    /// on every sweep of every phone the project has ever seen. What is
    /// worth knowing is the moment it stops being true.
    /// </summary>
    public bool HasHeat => _vitals?.Heat is { IsThrottling: true } && IsConnected;

    /// <summary>
    /// The heat, in one word.
    ///
    /// **Never in degrees.** The reading carries a surface temperature
    /// and the reference phone shows eighty-four while its thermal
    /// status is zero and nothing is throttled: a figure like that
    /// raises a false alarm on the one device the project actually
    /// tests on. The status, which is the verdict Android itself gives,
    /// says the same thing without inviting the mistake.
    /// </summary>
    public string HeatText => _vitals?.Heat switch
    {
        { Status: >= ThermalReading.Severe } => Strings.Get("VitalsHeatHot"),
        { Status: >= ThermalReading.Throttling } => Strings.Get("VitalsHeatWarm"),
        _ => string.Empty,
    };

    /// <inheritdoc cref="BatteryBrushKey" />
    public string HeatBrushKey => _vitals?.Heat switch
    {
        { Status: >= ThermalReading.Severe } => "DangerBrush",
        { Status: >= ThermalReading.Throttling } => "WarningBrush",
        _ => "TextMutedBrush",
    };

    /// <summary>
    /// True when the free space is worth a word, which is to say when
    /// there is not much of it left.
    ///
    /// **It used to show at all times, and said nothing.** The threshold
    /// is two gigabytes; the reference phones carry three hundred and
    /// fourteen. A figure that is always comfortable is a binary fact
    /// dressed as a continuous one, and it asks the reader to know the
    /// threshold before it means anything. Shown only below it, its mere
    /// presence is the message.
    /// </summary>
    public bool HasStorage => _vitals?.Storage is { IsLow: true } && IsConnected;

    /// <summary>The free space, "292 Go".</summary>
    public string StorageText => _vitals?.Storage is { } room
        ? Strings.Format("VitalsStorage", room.FreeGigabytes)
        : string.Empty;

    /// <inheritdoc cref="BatteryBrushKey" />
    public string StorageBrushKey => _vitals?.Storage switch
    {
        { FreeBytes: <= StorageReading.Critical } => "DangerBrush",
        { IsLow: true } => "WarningBrush",
        _ => "TextMutedBrush",
    };

    /// <summary>
    /// True when there is a Wi-Fi link to describe, which there is not
    /// over USB.
    /// </summary>
    public bool HasLink => _vitals?.Link is not null && IsConnected;

    /// <summary>
    /// What there is to know about the link, in the fewest words.
    ///
    /// **The colour and the text have to name the same thing.** Saying
    /// "5 GHz" in amber because the channel was crowded read as though
    /// 5 GHz were the fault, which is the opposite of the truth: it is
    /// the good band. When something is wrong, the cell now says what.
    ///
    /// The band comes before the crowding, which is the order the health
    /// check itself uses, so the cell and the sentence beside it can
    /// never name two different faults.
    /// </summary>
    public string LinkText => _vitals?.Link switch
    {
        { Is24GHz: true } => Strings.Format("VitalsBand", 2.4),
        { IsCrowded: true } crowded =>
            Strings.Format("VitalsLinkCrowded", Math.Round(crowded.RetryShare * 100)),
        not null => Strings.Format("VitalsBand", 5),
        _ => string.Empty,
    };

    /// <summary>The band and the speed it announces.</summary>
    public string LinkSummary => _vitals?.Link is { } link
        ? Strings.Format("VitalsLinkTip", link.Is24GHz ? 2.4 : 5, link.LinkSpeedMbps)
        : string.Empty;

    /// <summary>
    /// <inheritdoc cref="BatteryBrushKey" path="/summary" />
    ///
    /// A crowded channel and the shared band are both worth a colour,
    /// and the threshold for the first belongs to the link itself, so
    /// that this band and the findings cannot drift apart.
    /// </summary>
    public string LinkBrushKey => _vitals?.Link switch
    {
        { IsCrowded: true } => "WarningBrush",
        { Is24GHz: true } => "WarningBrush",
        _ => "TextMutedBrush",
    };

    /// <summary>
    /// Sets the four readings, and notifies the display.
    ///
    /// The early return is why <see cref="DeviceVitals" /> is a record:
    /// the sweep hands the same values over and over, since each one is
    /// cached upstream for a minute or more.
    /// </summary>
    public void SetVitals(DeviceVitals? vitals)
    {
        if (_vitals == vitals)
        {
            return;
        }

        _vitals = vitals;

        OnPropertyChanged(nameof(HasHeat));
        OnPropertyChanged(nameof(HeatText));
        OnPropertyChanged(nameof(HeatBrushKey));
        OnPropertyChanged(nameof(HasStorage));
        OnPropertyChanged(nameof(StorageText));
        OnPropertyChanged(nameof(StorageBrushKey));
        OnPropertyChanged(nameof(HasLink));
        OnPropertyChanged(nameof(LinkText));
        OnPropertyChanged(nameof(LinkSummary));
        OnPropertyChanged(nameof(LinkBrushKey));
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

        // The four vitals are gated on the connection as well, and
        // SetVitals returns early when the readings have not changed:
        // without this, a phone that came back would keep them hidden
        // until one of its four readings happened to move.
        OnPropertyChanged(nameof(HasHeat));
        OnPropertyChanged(nameof(HasStorage));
        OnPropertyChanged(nameof(HasLink));
    }

}

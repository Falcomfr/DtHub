using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Settings;

namespace DtHub.App.ViewModels;

/// <summary>
/// An instance of the game in a list, with its state and its settings.
/// </summary>
public sealed partial class InstanceRowViewModel : ObservableObject
{
    public InstanceRowViewModel(DofusInstance instance)
    {
        _instance = instance;
        _isEnabled = instance.IsEnabled;
        _isManaged = instance.IsManaged;
        _name = instance.DisplayName;
    }

    private bool _applying;

    [ObservableProperty]
    private DofusInstance _instance;

    /// <summary>Checked for automatic launch.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Checked if the window follows automatic placements: keyboard
    /// navigation, repositioning, side-by-side, size changes. Unchecked, it
    /// stays where it is and the rest arranges itself without it.
    /// </summary>
    [ObservableProperty]
    private bool _isManaged = true;

    /// <summary>
    /// The opposite, as the row presents it: a lock, off by default.
    ///
    /// The stored setting says what the window follows, which makes sense for
    /// code; the interface says what the user decides, and what they decide is
    /// to lock one window in place, not to free eight of them.
    /// </summary>
    public bool IsLocked
    {
        get => !IsManaged;
        set => IsManaged = !value;
    }

    /// <summary>Displayed name, editable.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>True if a window is open for this instance.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// True while an action is in progress on this instance. A restart chains
    /// the session's stop, the forced stop on the Android side, and the
    /// restart of scrcpy: several seconds, during which a greyed-out button
    /// does not say that something is happening.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isWorking;

    /// <summary>
    /// True when the phone carrying this instance is busy with another
    /// opening. The row was not clicked: it is waiting its turn.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isDeviceBusy;

    /// <summary>
    /// True when the row must show the indicator rather than its buttons,
    /// whether for its own work or that of a neighbour on the same phone.
    ///
    /// Two fields and not one: <see cref="IsWorking"/> also acts as a
    /// reentrancy guard and is reset to false in a finally block, which would
    /// otherwise turn off a neighbour's indicator.
    /// </summary>
    public bool IsBusy => IsWorking || IsDeviceBusy;

    public string Key => Instance.Key;

    /// <summary>
    /// True if this account opens in the tabbed frame.
    ///
    /// Toggling neither opens nor closes anything: it is the same window that
    /// gets housed in the frame or taken back out of it.
    /// </summary>
    [ObservableProperty]
    private bool _isTabbed;

    /// <summary>True for the row currently being dragged.</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>True when a drop here would insert just above.</summary>
    [ObservableProperty]
    private bool _dropAbove;

    /// <summary>True when a drop here would insert just below.</summary>
    [ObservableProperty]
    private bool _dropBelow;

    public string DeviceId => Instance.DeviceId;

    /// <summary>
    /// Path to the game's icon in the cache, when it could be extracted.
    ///
    /// A string and not an image: no interface type enters a view model here,
    /// and it is a converter that decodes it, once for all the rows that share
    /// the same file.
    ///
    /// Nothing resets it: the scan updates the rows instead of recreating
    /// them, so that an icon once set stays there and the list does not
    /// flicker.
    /// </summary>
    [ObservableProperty]
    private string? _iconPath;

    /// <summary>
    /// Device carrying this instance. It paints the header, when there is one.
    /// </summary>
    [ObservableProperty]
    private DeviceGroupViewModel? _device;

    /// <summary>
    /// True when this row opens a run of instances from the same device, and
    /// must therefore carry its name.
    /// </summary>
    [ObservableProperty]
    private bool _showDeviceHeader;

    /// <summary>
    /// True when this row is the first piece of its device. What applies to
    /// the device itself, such as breaking the association, is only shown
    /// there: there is no reason to offer it twice.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstOfDevice;

    public bool IsDeviceConnected => Instance.IsDeviceConnected;

    /// <summary>
    /// Originating Android profile, shown in the background.
    /// </summary>
    public string UserLabel => Strings.Format("ProfileOrigin", Instance.UserId, Instance.UserName);

    /// <summary>
    /// True when the displayed name no longer says which profile this is, that
    /// is, when the user has renamed it. Without a renaming, the reminder
    /// would repeat the name right above it and would cost a line for nothing.
    /// </summary>
    public bool ShowUserLabel =>
        !string.Equals(Name, Instance.UserName, StringComparison.Ordinal);

    /// <summary>Raised when a box is checked or a name changed.</summary>
    public event EventHandler<InstanceRowViewModel>? EnabledChanged;

    /// <summary>
    /// Raised when the window enters or leaves automatic placements.
    /// </summary>
    public event EventHandler<InstanceRowViewModel>? ManagedChanged;

    public event EventHandler<InstanceRowViewModel>? NameChanged;

    /// <summary>
    /// Raised when the account enters the tabbed frame or leaves it.
    /// </summary>
    public event EventHandler<InstanceRowViewModel>? TabbedChanged;

    /// <summary>Raised when the account changes quality tier.</summary>
    public event EventHandler<InstanceRowViewModel>? QualityChanged;

    /// <summary>Raised when the account changes in-game distance.</summary>
    public event EventHandler<InstanceRowViewModel>? ZoomChanged;

    /// <summary>
    /// The time played this week, "3 h 20", or <c>null</c> if there is none
    /// yet.
    ///
    /// Information only: no limit, no reminder. Someone playing five accounts
    /// eventually no longer knows which one they are really running.
    ///
    /// <c>null</c> and not empty: this is a tooltip, and WPF shows none for a
    /// null value, whereas an empty string would give a grey bubble with
    /// nothing in it.
    /// </summary>
    public string? PlaytimeLabel
    {
        get
        {
            var seconds = Instance.PlayedThisWeek;

            if (seconds < 60)
            {
                return null;
            }

            var span = TimeSpan.FromSeconds(seconds);

            return span.TotalHours >= 1
                ? Strings.Format("PlaytimeHours", (int)span.TotalHours, span.Minutes)
                : Strings.Format("PlaytimeMinutes", span.Minutes);
        }
    }

    /// <summary>
    /// Tier specific to this account, or <c>null</c> to follow the shared one.
    ///
    /// One account is played and four are watched: the main one deserves
    /// better than the mules, and what is spared on the mules is that much
    /// less processor, bandwidth, heat, and battery.
    /// </summary>
    [ObservableProperty]
    private StreamQuality? _quality;

    /// <summary>True while the tier is being written.</summary>
    public bool IsQualityPending { get; set; }

    /// <summary>
    /// True when the account has its own tier, so that it shows.
    /// </summary>
    public bool HasOwnQuality => Quality is not null;

    /// <summary>
    /// What the button displays: the tier, or nothing if it follows the shared
    /// one.
    /// </summary>
    public string QualityLabel => Quality switch
    {
        StreamQuality.Low => Strings.Get("QualityLowShort"),
        StreamQuality.Medium => Strings.Get("QualityMediumShort"),
        StreamQuality.Maximum => Strings.Get("QualityMaximumShort"),
        StreamQuality.Custom => Strings.Get("QualityCustomShort"),
        _ => string.Empty,
    };

    /// <summary>
    /// Gives its tier to the account, or returns it to the shared one with
    /// <c>null</c>.
    /// </summary>
    [RelayCommand]
    private void PickQuality(StreamQuality? quality) => Quality = quality;

    /// <summary>Returns the account to the shared setting.</summary>
    [RelayCommand]
    private void FollowSharedQuality() => Quality = null;

    /// <summary>
    /// Distance specific to this account, or <c>null</c> to follow the shared
    /// one.
    ///
    /// The motive is not the same as for the tier. The tier saves; distance
    /// decides what is seen. One wants ground on the account being played, and
    /// the mules whose health bar is all that gets watched do not need that.
    /// </summary>
    [ObservableProperty]
    private GameZoom? _zoom;

    /// <summary>True while the distance is being written.</summary>
    public bool IsZoomPending { get; set; }

    /// <summary>
    /// True when the account has its own distance, so that it shows.
    /// </summary>
    public bool HasOwnZoom => Zoom is not null;

    /// <summary>
    /// What the button displays: the distance, or nothing if it follows the
    /// shared one.
    /// </summary>
    public string ZoomLabel => Zoom switch
    {
        GameZoom.Widest => Strings.Get("ZoomVeryFar"),
        GameZoom.Wide => Strings.Get("ZoomFar"),
        GameZoom.Normal => Strings.Get("ZoomNormal"),
        GameZoom.Close => Strings.Get("ZoomClose"),
        _ => string.Empty,
    };

    /// <summary>
    /// The distance its window is currently running with, or <c>null</c> when
    /// it is closed.
    ///
    /// Set by the list, which gets it from the launcher: distance is a scrcpy
    /// startup argument, fixed for the whole session, so the chosen setting
    /// can therefore differ from the one shown.
    /// </summary>
    public GameZoom? RunningZoom
    {
        get => _runningZoom;
        set
        {
            if (_runningZoom == value)
            {
                return;
            }

            _runningZoom = value;
            OnPropertyChanged(nameof(ZoomWaitsForReopen));
        }
    }

    private GameZoom? _runningZoom;

    /// <summary>
    /// True when the open window is still running with a distance other than
    /// the chosen one.
    ///
    /// **This is the mention that was missing.** The shared setting closes and
    /// reopens windows to show itself right away; an account's own setting
    /// does not, because reopening disconnects the character. Saying nothing,
    /// the setting looked dead. It is not: it is waiting.
    /// </summary>
    public bool ZoomWaitsForReopen =>
        RunningZoom is { } running && Zoom is { } wanted && running != wanted;

    /// <summary>
    /// Gives its distance to the account, or returns it to the shared one with
    /// <c>null</c>.
    /// </summary>
    [RelayCommand]
    private void PickZoom(GameZoom? zoom) => Zoom = zoom;

    /// <summary>Returns the account to the shared distance.</summary>
    [RelayCommand]
    private void FollowSharedZoom() => Zoom = null;

    /// <summary>
    /// True as long as the entered name is not written. The periodic scan must
    /// not replace it with the old one in the meantime: the input would seem
    /// to cancel itself out.
    /// </summary>
    public bool IsRenaming { get; set; }

    /// <summary>
    /// True between the user's click and the end of the write.
    ///
    /// The periodic scan rebuilds the list from the settings, and used to
    /// overwrite the choice as long as it was not saved: the lock would reopen
    /// on its own a few seconds after being closed. Same mechanism as for
    /// renaming, and for the same reason.
    /// </summary>
    public bool IsManagedPending { get; set; }

    /// <summary>True while the tab toggle is being written.</summary>
    public bool IsTabbedPending { get; set; }

    /// <inheritdoc cref="IsManagedPending" />
    public bool IsEnabledPending { get; set; }

    public void Update(DofusInstance instance, bool isRunning)
    {
        Instance = instance;
        IsRunning = isRunning;

        if (!IsTabbedPending && IsTabbed != instance.IsTabbed)
        {
            _applying = true;

            try
            {
                IsTabbed = instance.IsTabbed;
            }
            finally
            {
                _applying = false;
            }
        }

        OnPropertyChanged(nameof(PlaytimeLabel));

        if (!IsQualityPending && Quality != instance.Quality)
        {
            // Write coming from settings: propagating it as a user choice
            // would trigger a write on every scan.
            _applying = true;

            try
            {
                Quality = instance.Quality;
            }
            finally
            {
                _applying = false;
            }
        }

        if (!IsZoomPending && Zoom != instance.Zoom)
        {
            // Write coming from settings: propagating it as a user choice
            // would trigger a write on every scan.
            _applying = true;

            try
            {
                Zoom = instance.Zoom;
            }
            finally
            {
                _applying = false;
            }
        }

        if (!IsManagedPending && IsManaged != instance.IsManaged)
        {
            // Write coming from settings: propagating it as a user choice
            // would trigger a write on every scan.
            _applying = true;

            try
            {
                IsManaged = instance.IsManaged;
            }
            finally
            {
                _applying = false;
            }
        }

        if (!IsRenaming && !string.Equals(Name, instance.DisplayName, StringComparison.Ordinal))
        {
            // Write coming from settings, not from the user: propagating it as
            // a renaming would trigger a write on every scan.
            _applying = true;

            try
            {
                Name = instance.DisplayName;
            }
            finally
            {
                _applying = false;
            }
        }

        OnPropertyChanged(nameof(IsDeviceConnected));
        OnPropertyChanged(nameof(UserLabel));
        OnPropertyChanged(nameof(ShowUserLabel));
    }

    partial void OnQualityChanged(StreamQuality? value)
    {
        OnPropertyChanged(nameof(QualityLabel));
        OnPropertyChanged(nameof(HasOwnQuality));

        if (_applying)
        {
            return;
        }

        IsQualityPending = true;
        QualityChanged?.Invoke(this, this);
    }

    partial void OnZoomChanged(GameZoom? value)
    {
        OnPropertyChanged(nameof(ZoomLabel));
        OnPropertyChanged(nameof(HasOwnZoom));
        OnPropertyChanged(nameof(ZoomWaitsForReopen));

        if (_applying)
        {
            return;
        }

        IsZoomPending = true;
        ZoomChanged?.Invoke(this, this);
    }

    partial void OnIsTabbedChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        IsTabbedPending = true;
        TabbedChanged?.Invoke(this, this);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        IsEnabledPending = true;
        EnabledChanged?.Invoke(this, this);
    }

    partial void OnIsManagedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLocked));

        if (!_applying)
        {
            IsManagedPending = true;
            ManagedChanged?.Invoke(this, this);
        }
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(ShowUserLabel));

        if (_applying)
        {
            return;
        }

        IsRenaming = true;
        NameChanged?.Invoke(this, this);
    }
}

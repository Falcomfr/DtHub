using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Devices;
using DtHub.Core.Localization;
using DtHub.Core.Settings;

namespace DtHub.App.ViewModels;

/// <summary>
/// List of phones and their instances, shared by the startup window and
/// by the Devices tab of the configurator. It updates on its own:
/// plugging in a phone is enough to make it appear.
/// </summary>
public sealed partial class InstanceListViewModel : ObservableObject
{
    private readonly GameLauncher _launcher;
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;

    public InstanceListViewModel(
        GameLauncher launcher,
        SettingsService settings,
        IDialogService dialogs,
        IAppIconProvider icons)
    {
        _launcher = launcher;
        _settings = settings;
        _dialogs = dialogs;
        _icons = icons;

        // Closing a game window has to show at once. Waiting for the sweep
        // left the list announcing, for up to three seconds, a window that
        // no longer existed.
        _launcher.SessionChanged += OnSessionChanged;
        _launcher.DeviceBusyChanged += OnDeviceBusyChanged;
    }

    private void OnSessionChanged(object? sender, Core.Scrcpy.ScrcpySession session)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(RefreshRunningState);
    }

    /// <summary>
    /// All the instances of reachable devices, in the intended order.
    ///
    /// A single list, with no distinction by device: the phone's name
    /// appears only where it changes.
    /// </summary>
    public ObservableCollection<InstanceRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Known devices with no row in the list: offline, or reachable but
    /// without the game. Without this reminder, plugging in a phone where
    /// the game is missing would produce nothing at all on screen.
    /// </summary>
    public ObservableCollection<DeviceGroupViewModel> InactiveDevices { get; } = [];

    /// <summary>
    /// Devices seen, by identifier. Each one is shared by its rows.
    /// </summary>
    private readonly Dictionary<string, DeviceGroupViewModel> _devices = new(StringComparer.Ordinal);

    /// <summary>
    /// Last discovery of instances, reused between two sweeps.
    /// </summary>
    private IReadOnlyList<Core.Dofus.DofusInstance>? _instances;

    /// <summary>
    /// Fingerprint of devices seen, to know when to rediscover.
    /// </summary>
    private string? _signature;

    private DateTimeOffset _discoveredAt;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// What the banner shows, one finding per line, or <c>null</c> to
    /// hide it.
    ///
    /// **Written by <see cref="ShowBanner(IEnumerable{BannerLine})" />
    /// and by nothing else.** There used to be three properties here:
    /// this one decided whether the banner appeared, a second gave the
    /// text, a third the colour, and they were written by different
    /// paths. A launch failure wrote only this one, so the banner
    /// opened on the health notice left by the previous sweep and the
    /// failure's own text was reachable by no path at all.
    ///
    /// **One finding per line, not the worst one followed by a
    /// bubble.** A phone once carried three at once: lock, clutter,
    /// and an unready battery. Only one showed, and fixing the first
    /// was needed just to learn that a second existed. That is exactly
    /// the flaw D120 refused by keeping the text visible rather than
    /// hidden on hover. Each line stays truncated to one line, and
    /// hovering gives the full text.
    /// </summary>
    [ObservableProperty]
    private string? _problem;

    /// <summary>
    /// True when one of the lines <em>shown</em> will cut the session
    /// short, as opposed to a mere annoyance. Only the icon's colour
    /// depends on it: the text itself stays the same.
    ///
    /// It used to come from the worst finding of the whole
    /// application, those shown under a device's name included, so a
    /// banner talking about something else went red.
    /// </summary>
    [ObservableProperty]
    private bool _problemIsSerious;

    /// <summary>
    /// Posts what the banner says, text and colour together, so the
    /// two can no longer disagree. The rule is in
    /// <see cref="ErrorBanner" />, where the tests reach it.
    /// </summary>
    private void ShowBanner(IEnumerable<BannerLine> lines)
    {
        var banner = ErrorBanner.Of(lines);

        Problem = banner.Text;
        ProblemIsSerious = banner.IsSerious;
    }

    /// <summary>
    /// A single notice, grave by no construction: an action that
    /// failed says what happened, it does not announce the end of a
    /// session.
    /// </summary>
    internal void ShowBanner(string? text) =>
        ShowBanner(string.IsNullOrWhiteSpace(text) ? [] : [BannerLine.Of(text)]);

    /// <summary>
    /// True during a drag and drop. The periodic sweep then holds off
    /// rebuilding the list, otherwise a card would disappear under the
    /// cursor.
    /// </summary>
    public bool IsReordering { get; set; }

    /// <summary>
    /// Pace of the device sweep, based on the chosen quality.
    /// </summary>
    public TimeSpan PollInterval => _launcher.Quality.DevicePoll;

    /// <summary>True as long as no phone is reachable.</summary>
    public bool HasNoConnectedDevice => !_devices.Values.Any(d => d.IsConnected);

    /// <summary>
    /// False as long as no sweep has taken place yet.
    ///
    /// An empty list before the first sweep looks, in every way, like an
    /// empty list afterward: in both cases nothing is there. Only the
    /// difference between "I have not looked" and "I looked, there is
    /// nothing" allows writing it on screen, and it is read nowhere else.
    /// </summary>
    private bool _scanned;

    /// <summary>
    /// The state of each device seen, for the connection verdict. Devices
    /// only remembered show up there as offline, which is fair: a phone
    /// that has been known and does not answer is not a detected phone.
    /// </summary>
    public IReadOnlyList<AdbDeviceState> DeviceStates => [.. _devices.Values.Select(d => d.State)];

    /// <summary>True if at least one phone answers.</summary>
    public bool HasConnectedDevice => !HasNoConnectedDevice;

    /// <summary>True if there is more than one instance to reorder.</summary>
    public bool CanReorder => Rows.Count > 1;

    /// <summary>True if there is at least one instance to show.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>
    /// True if there is at least one device with no instance to report.
    /// </summary>
    public bool HasInactiveDevices => InactiveDevices.Count > 0;

    /// <summary>
    /// True when the "no device found" card is warranted.
    ///
    /// It used to show as soon as no device answered, including when a
    /// phone was named right above, in orange, with the note "to be
    /// authorised on the phone". The screen then contradicted itself in
    /// the same column. A device seen, even silent, is worth more than
    /// the word "none": the row that names it already says what is
    /// missing.
    /// </summary>
    public bool ShowsNoDeviceCard => _scanned && HasNoConnectedDevice && !HasInactiveDevices;

    /// <summary>
    /// True when the tips about the game windows have something to
    /// point to.
    ///
    /// They talk about a window that freezes and a window where the
    /// mouse does nothing. With no phone reachable there is no window,
    /// and these two lines become just more text on a screen that has
    /// nothing to say. As with the other blocks, nothing is asserted
    /// before the first sweep.
    /// </summary>
    public bool ShowsWindowHelp => _scanned && !HasNoConnectedDevice;

    /// <summary>
    /// True as long as the first sweep has produced nothing yet.
    ///
    /// **The list does not stay empty without saying so.** On first
    /// launch, the application tries to reach every remembered phone at
    /// its last address, and a phone that is off makes the system wait
    /// several seconds. During that time, the screen had neither a
    /// device nor the "no device found" card, which is carefully not
    /// shown before knowing: nothing at all, then, and nothing said
    /// that it was working.
    /// </summary>
    public bool ShowsSearching => !_scanned;

    /// <summary>Number of instances checked for launch.</summary>
    public int EnabledCount => Rows.Count(i => i.IsEnabled);

    // Launch profiles

    /// <summary>The saved profiles, as they appear in the panel.</summary>
    public ObservableCollection<LaunchProfileRowViewModel> Profiles { get; } = [];

    /// <summary>True if there is at least one profile to show.</summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>
    /// Name of the profile kept for startup, empty if there is none.
    ///
    /// Taken from the row itself and not from the setting: this is how
    /// the button says exactly what the list shows in its accent,
    /// without a difference in case or spaces being able to make them
    /// diverge.
    /// </summary>
    public string ActiveProfileName { get; private set; } = string.Empty;

    /// <summary>True when a profile is kept for startup.</summary>
    public bool HasActiveProfile => ActiveProfileName.Length > 0;

    /// <summary>
    /// What the button carries: the profile's name, or the generic
    /// word.
    /// </summary>
    public string ProfilesButtonText => LaunchProfiles.ButtonLabel(ActiveProfileName);

    /// <summary>
    /// The button's tooltip. It names the profile in full when there is
    /// one, since the button itself truncates long names.
    /// </summary>
    public string ProfilesTooltip => HasActiveProfile
        ? Strings.Format("ProfilesActiveTip", ActiveProfileName)
        : Strings.Get("ProfilesTip");

    /// <summary>
    /// Refreshes the saved profiles.
    ///
    /// Nothing is selected here: each row carries its own actions, and
    /// it is the button that opens, not the act of picking the row. The
    /// list can therefore rebuild itself on every sweep without
    /// triggering anything, which was not the case when selecting used
    /// to mean opening.
    /// </summary>
    private async Task SyncProfilesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);
        var byDefault = LaunchProfiles.Normalize(settings.DefaultLaunchProfile);

        Profiles.Clear();

        foreach (var profile in settings.LaunchProfiles)
        {
            Profiles.Add(new LaunchProfileRowViewModel(
                profile.Name,
                LaunchProfiles.Describe(profile, settings.Instances),
                string.Equals(profile.Name, byDefault, StringComparison.OrdinalIgnoreCase)));
        }

        ActiveProfileName = Profiles.FirstOrDefault(p => p.IsDefault)?.Name ?? string.Empty;

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(HasActiveProfile));
        OnPropertyChanged(nameof(ProfilesButtonText));
        OnPropertyChanged(nameof(ProfilesTooltip));
    }

    /// <summary>
    /// Opens a profile: whatever is not part of it closes, whatever it
    /// is missing opens.
    /// </summary>
    [RelayCommand]
    private async Task OpenProfileAsync(LaunchProfileRowViewModel? profile)
    {
        if (profile is null)
        {
            return;
        }

        // Closing game windows is not done without saying so: you could
        // be in the middle of a game, and a click is not consent.
        if (_launcher.ActiveSessions.Count > 0
            && !_dialogs.Confirm(
                Strings.Format("OpenProfileQuestion", profile.Name)
                + "\n\n" + Strings.Get("ProfileClosesOthers"),
                Strings.Get("OpenProfileTitle")))
        {
            return;
        }

        IsBusy = true;

        try
        {
            // Close first, apply after: closing starts by recording the
            // geometry of the open windows, and would therefore overwrite
            // the positions the profile has just set.
            await _launcher.CloseAllAsync().ConfigureAwait(true);
            await _settings.ApplyLaunchProfileAsync(profile.Name).ConfigureAwait(true);

            var report = await _launcher.LaunchEnabledAsync().ConfigureAwait(true);

            if (report.Problems.Count > 0)
            {
                _dialogs.ShowWarning(string.Join(Environment.NewLine, report.Problems));
            }
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Remembers the open accounts, their positions and the settings,
    /// under a name.
    /// </summary>
    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        // The geometry is recorded before the snapshot; otherwise the
        // profile would remember the positions from opening and not
        // those of the moment: moving a window and then creating would
        // have remembered nothing.
        await _launcher.CaptureGeometriesAsync().ConfigureAwait(true);

        var settings = await _settings.GetAsync().ConfigureAwait(true);

        var open = _launcher.ActiveSessions
            .Select(s => s.Target.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Failing an open window, the startup set is authoritative: it
        // is the one that will reopen, and so it is the one that gets
        // saved.
        if (open.Count == 0)
        {
            open = [.. settings.Instances.Where(i => i.IsEnabled).Select(i => i.Key)];
        }

        if (open.Count == 0)
        {
            _dialogs.ShowWarning(
                Strings.Get("NothingToRemember"),
                Strings.Get("CreateProfile"));

            return;
        }

        var tabbed = settings.Instances.Count(
            i => i.IsTabbed && open.Contains(i.Key, StringComparer.Ordinal));

        if (_dialogs.PromptText(
                Strings.Get("ProfileNameQuestion"),
                null,
                Strings.Get("CreateProfile"),
                LaunchProfiles.Announce(
                    open.Count,
                    settings.Quality,
                    settings.GameZoom,
                    tabbed,
                    settings.AudioEnabled),
                Strings.Get("Create")) is not { } typed)
        {
            return;
        }

        if (!await _settings.SaveLaunchProfileAsync(typed, open).ConfigureAwait(true))
        {
            _dialogs.ShowWarning(Strings.Get("ProfileNeedsName"), Strings.Get("CreateProfile"));
            return;
        }

        await SyncProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>Sets the startup profile, or removes it.</summary>
    [RelayCommand]
    private async Task ToggleDefaultProfileAsync(LaunchProfileRowViewModel? profile)
    {
        if (profile is null)
        {
            return;
        }

        await _settings
            .SetDefaultLaunchProfileAsync(profile.IsDefault ? null : profile.Name)
            .ConfigureAwait(true);

        await SyncProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>Deletes a profile, after confirmation.</summary>
    [RelayCommand]
    private async Task DeleteProfileAsync(LaunchProfileRowViewModel? profile)
    {
        if (profile is null
            || !_dialogs.Confirm(
                Strings.Format("DeleteProfileQuestion", profile.Name)
                + "\n\n" + Strings.Get("OnlyTheRowGoes"),
                Strings.Get("DeleteProfileTitle")))
        {
            return;
        }

        await _settings.DeleteLaunchProfileAsync(profile.Name).ConfigureAwait(true);

        await SyncProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// True if this serial number is that of this instance's device.
    /// </summary>
    private static bool Carries(DeviceDiscoveryResult discovery, string serial, string deviceId) =>
        discovery.Devices.Any(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal)
            && string.Equals(d.Serial, serial, StringComparison.Ordinal));

    /// <summary>Sweeps the phones and rebuilds the list.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsReordering)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var discovery = await _launcher.RefreshDevicesAsync(cancellationToken).ConfigureAwait(true);

            foreach (var device in discovery.Devices)
            {
                if (!_devices.TryGetValue(device.Id, out var view))
                {
                    view = new DeviceGroupViewModel(device.Id, device.DisplayName);
                    _devices[device.Id] = view;
                }

                view.Update(device);
            }

            foreach (var gone in _devices.Keys
                .Where(id => !discovery.Devices.Any(d => string.Equals(d.Id, id, StringComparison.Ordinal)))
                .ToList())
            {
                _devices.Remove(gone);
            }

            // Listing devices is cheap; rediscovering instances is not,
            // each profile of each device asking the phone two commands.
            // So it is only redone if the set of devices has changed, or
            // after a long while.
            var signature = string.Join(
                "|",
                discovery.Devices.Select(d => $"{d.Id}:{d.State}").Order(StringComparer.Ordinal));

            var looking = _instances is null
                || !string.Equals(signature, _signature, StringComparison.Ordinal)
                || DateTimeOffset.UtcNow - _discoveredAt >= _launcher.Quality.InstanceRediscovery;

            if (looking)
            {
                // The phones are shown before their accounts are looked for.
                // That search was measured at 2.9 seconds, it is the longest
                // thing the sweep does, and none of it says which phones are
                // there: they are known already.
                //
                // The list handed over is the one we hold, null included. Null
                // means the search has not answered, and a pass that does not
                // know writes no verdict: the phones appear, and whatever was
                // last established about them stays. It used to be `?? []`, and
                // an empty list is an answer, so every phone was stamped "game
                // not installed" for those 2.9 seconds, every time one of ten
                // ordinary gestures invalidated the cache.
                ShowList(discovery, _instances);

                _instances = await _launcher.RefreshInstancesAsync(cancellationToken).ConfigureAwait(true);
                _signature = signature;
                _discoveredAt = DateTimeOffset.UtcNow;
            }

            // The looking branch above has just filled it; when it did not run,
            // `looking` was false, which is only possible with a list in hand.
            var instances = _instances;

            ShowList(discovery, instances);

            // Session summaries quote the account names: they are redone
            // here, after the list has been rebuilt.
            await SyncProfilesAsync(cancellationToken).ConfigureAwait(true);

            RequestIcons();

            // What discovery found is on screen now. The readings kept from the
            // previous sweep are applied straight away, so a phone that already
            // had a gauge does not lose it while the new one is fetched.
            ApplyHealth(discovery, instances);

            // Then the slow pass, off the critical path. It asks each phone six
            // questions in turn, measured at 2.2 seconds for two devices, and
            // it used to run before any of the above: the list waited on it for
            // nothing, since none of its answers say which phones are there.
            // Awaiting here hands the dispatcher back, so the list is painted
            // before the questions are asked.
            await _launcher.RefreshHealthAsync(discovery, cancellationToken).ConfigureAwait(true);

            ApplyHealth(discovery, instances);
        }
        catch (AdbException exception)
        {
            ShowBanner(exception.UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Puts on screen what is known of the phones and of their accounts.
    ///
    /// Called twice on a sweep that has to look for accounts: once with the
    /// phones alone, so that they appear without waiting on a search measured
    /// at 2.9 seconds, and once with what that search found.
    /// </summary>
    private void ShowList(
        DeviceDiscoveryResult discovery,
        IReadOnlyList<Core.Dofus.DofusInstance>? instances)
    {
        ApplyPresence(discovery.Devices, instances);

        // The rows are left exactly as the last authoritative pass built
        // them. Rebuilding them from a stale list would be worse than not
        // touching them: `InstanceRowViewModel.Update` writes the quality,
        // the distance and the name back from the instance, so a pass run
        // on an old list would revert the setting the user has just
        // changed, for the 2.9 seconds the search takes.
        if (instances is not null)
        {
            // Only the instances of reachable phones have a row. The other
            // devices do not disappear for all that: they are recalled
            // separately, with the reason.
            var connected = discovery.Devices
                .Where(d => d.IsConnected)
                .ToDictionary(d => d.Id, StringComparer.Ordinal);

            SyncRows([.. instances.Where(i => connected.ContainsKey(i.DeviceId))]);
            RefreshDeviceHeaders();
        }

        RefreshBusyState();
        SyncInactiveDevices(discovery.Devices);

        _scanned = true;

        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasInactiveDevices));
        OnPropertyChanged(nameof(HasNoConnectedDevice));
        OnPropertyChanged(nameof(ShowsNoDeviceCard));
        OnPropertyChanged(nameof(ShowsSearching));
        OnPropertyChanged(nameof(ShowsWindowHelp));
        OnPropertyChanged(nameof(HasConnectedDevice));
        OnPropertyChanged(nameof(EnabledCount));
    }

    /// <summary>
    /// Records what this pass established about each phone, and nothing
    /// more.
    ///
    /// The verdict is rendered by <see cref="Core.Dofus.GamePresenceReading" />
    /// in the core, where it is put to the test. A null list means the search
    /// has not answered, and nothing is read into a silence nobody asked for.
    /// </summary>
    private void ApplyPresence(
        IReadOnlyList<Core.Devices.AndroidDevice> devices,
        IReadOnlyList<Core.Dofus.DofusInstance>? instances)
    {
        var known = _devices.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Presence,
            StringComparer.Ordinal);

        foreach (var (id, presence) in
            Core.Dofus.GamePresenceReading.After(known, devices, instances))
        {
            if (_devices.TryGetValue(id, out var view))
            {
                view.SetPresence(presence);
            }
        }
    }

    /// <summary>
    /// Puts the health readings on screen: the banner, the bubble behind it,
    /// and the gauge and findings under each phone.
    ///
    /// Called twice per sweep, once with whatever the previous pass left and
    /// once with the fresh readings. The questions put to the phones take
    /// seconds, and none of their answers say which phones are there, so the
    /// list is shown first and this fills it in.
    /// </summary>
    private void ApplyHealth(
        DeviceDiscoveryResult discovery,
        IReadOnlyList<Core.Dofus.DofusInstance>? instances)
    {
        // The incidents from device discovery and those from the
        // instance sweep share the same banner: an unreadable profile is
        // just as worth knowing as an unreachable device.
        var warnings = discovery.Warnings.Concat(_launcher.InstanceWarnings);

        // Each line carries its own severity, and the banner's colour
        // is the worst of the lines it actually shows. A discovery
        // warning is worth knowing without ending anything.
        List<BannerLine> lines = [.. warnings.Select(BannerLine.Of)];

        // Each device's summary shows under its name, in addition to
        // this banner. Except for a device with no row in the list: it
        // has no header to lodge its finding under, and losing it would
        // be worse than putting it in the wrong place. That one is
        // named, since nothing around it names it.
        lines.AddRange(_launcher.HealthByDevice
            .Where(pair => !(instances ?? []).Any(i => Carries(discovery, pair.Key, i.DeviceId)))
            .Select(pair => new BannerLine(
                pair.Value.Device is { Length: > 0 } named
                    ? Strings.Format("NamedFinding", named, pair.Value.Text)
                    : pair.Value.Text,
                pair.Value.Serious ? HealthSeverity.Serious : HealthSeverity.Warning)));

        // The recovery of a lost window is said in the same place, and
        // for the same reason: it explains what has just happened. It
        // announces no end of session, so it does not redden anything.
        if (_launcher.RecoveryNotice is { Length: > 0 } recovery)
        {
            lines.Add(new BannerLine(recovery, HealthSeverity.Notice));
        }

        // The game left running on the phone is said there too: its
        // window has disappeared, and nothing else on screen can report
        // it.
        if (_launcher.StopFailedNotice is { Length: > 0 } left)
        {
            lines.Add(BannerLine.Of(left));
        }

        ShowBanner(lines);

        foreach (var device in discovery.Devices)
        {
            if (!_devices.TryGetValue(device.Id, out var view))
            {
                continue;
            }

            view.SetBattery(_launcher.Batteries.GetValueOrDefault(device.Serial));

            var found = _launcher.HealthByDevice.GetValueOrDefault(device.Serial);

            view.SetProblems(found?.Text, found?.Serious ?? false);
            view.SetNeedsPairing(_launcher.NeedsPairing.Contains(device.Id));
        }
    }

    private readonly IAppIconProvider _icons;

    /// <summary>
    /// Requests the icon for rows that do not have one yet.
    ///
    /// What is already known is set right away, without asking the phone
    /// anything: the sweep runs every three seconds and must not be
    /// lengthened by a single command. The rest goes off as a background
    /// task that nothing awaits, which only the provider's promise never
    /// to throw allows.
    /// </summary>
    private void RequestIcons()
    {
        foreach (var row in Rows)
        {
            if (row.IconPath is not null)
            {
                continue;
            }

            if (_icons.Find(row.DeviceId, row.Instance.PackageName) is { } known)
            {
                row.IconPath = known;

                continue;
            }

            if (_devices.TryGetValue(row.DeviceId, out var device) && device.Serial.Length > 0)
            {
                _ = FillIconAsync(row, device.Serial);
            }
        }
    }

    private async Task FillIconAsync(InstanceRowViewModel row, string serial)
    {
        var path = await _icons.GetAsync(
            new AppIconRequest(
                row.DeviceId,
                serial,
                row.Instance.UserId,
                row.Instance.PackageName)).ConfigureAwait(true);

        if (path is not null)
        {
            row.IconPath = path;
        }
    }

    private InstanceRowViewModel? _hinted;
    private bool _hintedAbove;

    /// <summary>Clears all the drop hints.</summary>
    public void ClearDropHints()
    {
        _hinted = null;

        foreach (var row in Rows)
        {
            row.IsDragging = false;
            row.DropAbove = false;
            row.DropBelow = false;
        }
    }

    /// <summary>
    /// Marks the spot where the drop would insert, above or below the
    /// row being hovered. Only one hint is visible at a time.
    ///
    /// Nothing is touched when the hint has not changed place: hovering
    /// triggers dozens of events per second, and resetting everything on
    /// each one made the line flicker.
    /// </summary>
    public void ShowDropHint(InstanceRowViewModel onto, bool above)
    {
        ArgumentNullException.ThrowIfNull(onto);

        if (ReferenceEquals(_hinted, onto) && _hintedAbove == above)
        {
            return;
        }

        ClearDropHints();

        _hinted = onto;
        _hintedAbove = above;

        onto.DropAbove = above;
        onto.DropBelow = !above;
    }

    /// <summary>
    /// Drops an instance just before or just after another one.
    /// </summary>
    public async Task ReorderAsync(InstanceRowViewModel dragged, InstanceRowViewModel onto, bool above)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        ArgumentNullException.ThrowIfNull(onto);

        if (ReferenceEquals(dragged, onto))
        {
            return;
        }

        if (await _settings.MoveInstanceAsync(dragged.Key, onto.Key, above).ConfigureAwait(true))
        {
            // The discovery cache carries the old order: keeping it
            // would bring the row back to its place under the cursor. Any
            // write to the settings must invalidate it.
            _instances = null;

            // The order of the list drives the order of the windows:
            // without this, moving a row only changed the keyboard path.
            await _launcher.RefreshRanksAsync().ConfigureAwait(true);

            await RefreshAsync().ConfigureAwait(true);

            // The open windows are reopened in the new order.
            //
            // Bringing the stack back up was enough for Alt+Tab, but not
            // for the taskbar thumbnails: Windows arranges them in
            // creation order and exposes nothing to change it. Recreating
            // them is the only way, and it is what the user knowingly
            // asked for.
            await _launcher.ReopenAsync().ConfigureAwait(true);
            await _launcher.ApplyWindowOrderAsync().ConfigureAwait(true);
        }
    }

    private void OnDeviceBusyChanged(object? sender, Core.Scrcpy.DeviceBusyChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            // The lock has just been taken: it knows better than we do,
            // and above all it will release sooner. The engagement only
            // covered the wait before it, and has nothing left to say.
            if (args.IsBusy)
            {
                _ = _engages.Remove(args.DeviceId);
            }

            RefreshBusyState();
        });
    }

    /// <summary>
    /// Turns the indicator back on for rows whose phone is busy.
    ///
    /// The state is reread from the launcher on every pass, never
    /// remembered here: the list rebuilds itself every few seconds, and
    /// a new row must be born in the right state.
    /// </summary>
    public void RefreshBusyState()
    {
        foreach (var row in Rows)
        {
            row.IsDeviceBusy = _launcher.IsDeviceBusy(row.DeviceId) || _engages.Contains(row.DeviceId);
        }
    }

    /// <summary>
    /// Devices on which an action has just been requested, but whose
    /// opening lock is not yet taken.
    ///
    /// The lock is only taken at the end of the opening preamble:
    /// rereading the accounts, discovering the devices, reading the
    /// connection. Measured on this machine, two seconds and three
    /// tenths between the click and the taking. During all that time,
    /// the device was not officially busy, and the buttons of the other
    /// accounts therefore stayed clickable while an opening was already
    /// under way.
    ///
    /// The engagement is taken the instant of the click, without waiting
    /// for anything, and released when the action ends. The lock remains
    /// the sole judge of who goes through: this only tells the screen
    /// what is already decided.
    /// </summary>
    private readonly HashSet<string> _engages = new(StringComparer.Ordinal);

    /// <summary>
    /// Refreshes only the open or closed state of each instance.
    /// </summary>
    public void RefreshRunningState()
    {
        foreach (var row in Rows)
        {
            row.IsRunning = _launcher.IsOpen(row.Instance);
        }
    }

    /// <summary>Opens an instance that is not open yet.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LaunchInstanceAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, instance => _launcher.LaunchAsync([instance]), engageDevice: true);

    /// <summary>Closes the game on the device, then reopens it.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RestartAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, instance => _launcher.RestartAsync(instance), engageDevice: true);

    /// <summary>
    /// Closes an instance's window.
    ///
    /// Without engaging the device: closing does not go through the
    /// opening lock, two closures do not get in each other's way, and
    /// greying out the neighbours would force closing one account at a
    /// time.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StopAsync(InstanceRowViewModel? row) =>
        ActOnAsync(
            row,
            async instance =>
            {
                await _launcher.StopAsync(instance).ConfigureAwait(true);
                return new LaunchReport(0, []);
            },
            engageDevice: false);

    /// <summary>
    /// Runs an action on an instance and reflects the result onto the
    /// list. The safeguard is the same for the three buttons: they talk
    /// to the device, and two concurrent actions would leave the
    /// displayed state out of step with the windows actually open.
    /// </summary>
    /// <param name="engageDevice">
    /// True for the actions that will take the opening lock. Only they
    /// grey out the neighbours of the same phone, and only until the
    /// lock takes over.
    /// </param>
    private async Task ActOnAsync(
        InstanceRowViewModel? row,
        Func<Core.Dofus.DofusInstance, Task<LaunchReport>> action,
        bool engageDevice)
    {
        // The safeguard belongs to the row, not to the whole list. A
        // global lock used to swallow the click when another instance was
        // working, or simply during the periodic sweep: you then had to
        // click a second time.
        if (row is null || row.IsWorking)
        {
            return;
        }

        row.IsWorking = true;

        // Before the first await: that is the whole point. The
        // neighbours of the same phone grey out in the same paint pass as
        // the clicked row, and not two seconds later.
        if (engageDevice)
        {
            _ = _engages.Add(row.DeviceId);
            RefreshBusyState();
        }

        try
        {
            var report = await action(row.Instance).ConfigureAwait(true);

            ShowBanner(report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null);
        }
        catch (AdbException exception)
        {
            ShowBanner(exception.UserMessage);
        }
        finally
        {
            row.IsWorking = false;
            _ = _engages.Remove(row.DeviceId);

            // The state is reread rather than deduced from the action: a
            // session may have stopped on its own in the meantime.
            RefreshRunningState();
            RefreshBusyState();

            // An action may have changed what the device carries: the
            // next sweep rediscovers rather than reusing the cache.
            _instances = null;
        }
    }

    private void SyncRows(IReadOnlyList<Core.Dofus.DofusInstance> instances)
    {
        foreach (var instance in instances)
        {
            var row = Rows.FirstOrDefault(r => r.Key == instance.Key);

            if (row is null)
            {
                row = new InstanceRowViewModel(instance) { IsRunning = _launcher.IsOpen(instance) };
                row.EnabledChanged += OnEnabledChanged;
                row.ManagedChanged += OnManagedChanged;
                row.TabbedChanged += OnTabbedChanged;
                row.QualityChanged += OnQualityChanged;
                row.ZoomChanged += OnZoomChanged;
                row.NameChanged += OnNameChanged;
                Rows.Add(row);
            }
            else
            {
                row.Update(instance, _launcher.IsOpen(instance));
            }

            row.Device = _devices.GetValueOrDefault(instance.DeviceId);

            // The distance at which its window runs, so that a setting
            // waiting for the next opening says so instead of looking
            // dead. Nothing to show when the window is closed.
            row.RunningZoom = row.IsRunning ? _launcher.ZoomInUse(instance.Key) : null;
        }

        // Rows already present did not move: the saved order therefore
        // only showed at the next startup. Moving rather than clearing: a
        // Clear would break a drag and drop in progress.
        for (var position = 0; position < instances.Count; position++)
        {
            var row = Rows.FirstOrDefault(
                r => string.Equals(r.Key, instances[position].Key, StringComparison.Ordinal));

            if (row is not null && Rows.IndexOf(row) is var current
                && current != position && position < Rows.Count)
            {
                Rows.Move(current, position);
            }
        }

        foreach (var stale in Rows.Where(r => !instances.Any(i => i.Key == r.Key)).ToList())
        {
            Rows.Remove(stale);
        }
    }

    /// <summary>
    /// Recalls the devices with no row, with the reason: offline, or
    /// reachable but without the game.
    /// </summary>
    private void SyncInactiveDevices(IReadOnlyList<Core.Devices.AndroidDevice> devices)
    {
        // A phone has no row when it is unreachable, or when the search
        // answered that it carries no game. A phone we have not asked about
        // yet is inactive too, and says so in its own words.
        var inactive = devices
            .Where(d => !d.IsConnected
                || !_devices.TryGetValue(d.Id, out var view)
                || view.Presence != Core.Dofus.GamePresence.Present)
            .Select(d => d.Id)
            .ToList();

        foreach (var id in inactive)
        {
            var view = _devices[id];

            if (!InactiveDevices.Contains(view))
            {
                InactiveDevices.Add(view);
            }
        }

        foreach (var stale in InactiveDevices
            .Where(d => !inactive.Contains(d.DeviceId, StringComparer.Ordinal))
            .ToList())
        {
            InactiveDevices.Remove(stale);
        }
    }

    /// <summary>
    /// Sets the device name where the device changes, and what only
    /// holds once per phone, on its first segment.
    /// </summary>
    private void RefreshDeviceHeaders()
    {
        var ids = Rows.Select(r => r.DeviceId).ToList();

        var headers = DeviceHeaders.For(ids);
        var firsts = DeviceHeaders.FirstOccurrences(ids);

        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].ShowDeviceHeader = headers[i];
            Rows[i].IsFirstOfDevice = firsts[i];
        }
    }

    /// <summary>
    /// Adds an account on this phone.
    ///
    /// A fresh Android profile, with the game inside it. This is the
    /// mechanism of Android's multiple accounts, the very one the
    /// phone's overlay uses itself: nothing is copied, the application
    /// stays the publisher's own.
    ///
    /// The name is set by default and can then be changed like that of
    /// the other instances: asking for a name before even knowing
    /// whether the phone will accept it would make you type for
    /// nothing.
    ///
    /// A confirmation is asked, because this touches the phone and the
    /// profile is born empty: the game will ask again for its resources
    /// and the login, which is not what you expect from a click on a
    /// plus sign.
    /// </summary>
    [RelayCommand]
    private async Task AddAccountAsync(DeviceGroupViewModel? device)
    {
        if (device is null)
        {
            return;
        }

        var name = NextAccountName(device.DeviceId);

        if (!_dialogs.Confirm(
                Strings.Format("AddAccountQuestion", name, device.Name)
                + "\n\n" + Strings.Get("AddAccountConsequence"),
                Strings.Get("AddAccountTitle")))
        {
            return;
        }

        device.IsBusy = true;

        try
        {
            var result = await _launcher
                .AddAccountAsync(device.DeviceId, name)
                .ConfigureAwait(true);

            if (result.Succeeded)
            {
                _dialogs.ShowInformation(result.Message, Strings.Get("AddAccountTitle"));
            }
            else
            {
                _dialogs.ShowWarning(result.Message, Strings.Get("AddAccountTitle"));
            }
        }
        finally
        {
            device.IsBusy = false;
        }

        // The cache is discarded before refreshing: otherwise the sweep
        // resumes what it already knows, and the account just created
        // only appears after the rediscovery interval, fifteen to sixty
        // seconds depending on the tier. The same gesture already exists
        // in ActOnAsync.
        _instances = null;

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// A free name for the next account on this phone. The number
    /// follows what already exists, never falling back onto a name
    /// already taken: two profiles with the same name would be
    /// indistinguishable in the list as on the phone.
    /// </summary>
    private string NextAccountName(string deviceId)
    {
        var taken = Rows
            .Where(r => string.Equals(r.DeviceId, deviceId, StringComparison.Ordinal))
            .Select(r => r.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        for (var n = taken.Count + 1; ; n++)
        {
            var candidate = Strings.Format("DefaultAccountName", n);

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Breaks the association with a device. Its windows close, its
    /// instances and their settings are erased: it will have to be
    /// paired again to be used once more.
    /// </summary>
    [RelayCommand]
    private async Task ForgetDeviceAsync(DeviceGroupViewModel? device)
    {
        if (device is null
            || !_dialogs.Confirm(
                Strings.Format("ForgetDeviceQuestion", device.Name)
                + "\n\n" + Strings.Get("ForgetDeviceConsequence"),
                Strings.Get("ForgetDeviceTitle")))
        {
            return;
        }

        await _launcher.ForgetDeviceAsync(device.DeviceId).ConfigureAwait(true);
        await _settings.ForgetDeviceAsync(device.DeviceId).ConfigureAwait(true);

        // **The device leaves the view here, and not at the next
        // sweep.**
        //
        // It used to take clicking twice to remove it, and it showed up
        // between the two "game not installed" entries, which was
        // wrong. The two symptoms share the same cause: "RefreshAsync"
        // returns without doing anything when a sweep is already under
        // way, and one leaves every two seconds. The call below
        // therefore almost never ran, and yet the caller believed it had
        // refreshed.
        //
        // The sweep in flight, for its part, finished with the device
        // list from before the break but the instances already erased:
        // the device reappeared with no account at all, so marked as
        // lacking the game. An absence of information shown as a
        // finding.
        //
        // We know what we have just done: we remove it, without waiting
        // for anyone.
        Forget(device);

        // The cache is discarded, otherwise the sweep resumes what it
        // already knows and the device's rows stay on screen until the
        // rediscovery interval. Same gesture as for adding an account.
        _instances = null;

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Removes from the view everything that belonged to a device.
    ///
    /// The three collections, because a device can be in any of them:
    /// its accounts in <see cref="Rows" />, its card in
    /// <see cref="InactiveDevices" /> when it has none, and its entry in
    /// the index used to find them.
    /// </summary>
    private void Forget(DeviceGroupViewModel device)
    {
        foreach (var row in Rows.Where(r => r.DeviceId == device.DeviceId).ToList())
        {
            _ = Rows.Remove(row);
        }

        _ = InactiveDevices.Remove(device);
        _ = _devices.Remove(device.DeviceId);
    }

    private async void OnEnabledChanged(object? sender, InstanceRowViewModel row)
    {
        try
        {
            await _settings.SetInstanceEnabledAsync(row.Key, row.IsEnabled).ConfigureAwait(true);

            // The discovery cache carries the old value: keeping it
            // would bring the checkbox back to its previous state at the
            // next sweep.
            _instances = null;
        }
        finally
        {
            row.IsEnabledPending = false;
        }

        OnPropertyChanged(nameof(EnabledCount));
    }

    private async void OnManagedChanged(object? sender, InstanceRowViewModel row)
    {
        try
        {
            await _settings.SetInstanceManagedAsync(row.Key, row.IsManaged).ConfigureAwait(true);

            // Same trap as for sorting and for the checkboxes: any write
            // to the settings must invalidate the cache, otherwise the
            // lock reopens on its own at the next sweep.
            _instances = null;

            // The launcher rereads the list of set-aside ones at the
            // next placement.
            await _launcher.RefreshRanksAsync().ConfigureAwait(true);
        }
        finally
        {
            row.IsManagedPending = false;
        }
    }

    /// <summary>
    /// The account enters the tabbed frame or leaves it.
    ///
    /// The launcher takes care of everything: it writes the setting,
    /// then docks or undocks the window if it is open. Nothing is
    /// reopened.
    /// </summary>
    /// <summary>
    /// Writes the quality tier specific to an account.
    ///
    /// The new tier will only take effect the next time the window
    /// opens: the resolution and the bitrate are fixed when scrcpy
    /// launches, and a session already running does not renegotiate.
    /// </summary>
    private async void OnQualityChanged(object? sender, InstanceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            await _settings.SetInstanceQualityAsync(row.Key, row.Quality).ConfigureAwait(true);

            // Same trap as for the lock: without this the next sweep
            // would give the row back its old tier.
            _instances = null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowBanner(exception.Message);
        }
        finally
        {
            row.IsQualityPending = false;
        }
    }

    private async void OnTabbedChanged(object? sender, InstanceRowViewModel row)
    {
        try
        {
            await _launcher.SetTabbedAsync(row.Instance, row.IsTabbed).ConfigureAwait(true);

            // Same trap as for the lock: without this the next sweep
            // would give the row back its old state.
            _instances = null;
        }
        catch (AdbException exception)
        {
            ShowBanner(exception.UserMessage);
        }
        finally
        {
            row.IsTabbedPending = false;
        }
    }

    private async void OnZoomChanged(object? sender, InstanceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            await _settings.SetInstanceZoomAsync(row.Key, row.Zoom).ConfigureAwait(true);

            // Same trap as for the tier: without this the next sweep
            // would give the row back its old distance.
            _instances = null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowBanner(exception.Message);
        }
        finally
        {
            row.IsZoomPending = false;
        }
    }

    private async void OnNameChanged(object? sender, InstanceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            await _settings.RenameInstanceAsync(row.Key, row.Name).ConfigureAwait(true);

            // Same trap as for the tier and the distance, and the one this
            // handler was the only one not to avoid. The cached list still
            // carries the old name, and Update puts it back on the row at the
            // next sweep, two to six seconds later; the written name then only
            // returns at the next full rediscovery, fifteen to sixty seconds
            // further on. D150 named this trap and spared the other settings
            // from it.
            _instances = null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The siblings all report a failed write. This one swallowed it,
            // so a rename that never reached the disk looked like one that had.
            ShowBanner(exception.Message);
        }
        finally
        {
            // The name is written: the sweep can be trusted again.
            row.IsRenaming = false;
        }
    }
}

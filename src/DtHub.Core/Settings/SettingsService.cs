using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.Core.Settings;

/// <summary>
/// Single access point for settings. Loaded once, kept in memory, and
/// written on every change: there is no Save button to forget.
/// </summary>
public sealed class SettingsService : IDisposable
{
    /// <summary>
    /// Sizes shipped up to schema 3. The first one was too large.
    /// </summary>
    private static readonly int[] LegacySizePercentages = [55, 70, 85, 100];

    private readonly IDocumentStore<AppSettingsDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppSettingsDocument? _current;

    public SettingsService(IDocumentStore<AppSettingsDocument> store) => _store = store;

    /// <summary>Fired after every successful write.</summary>
    public event EventHandler<AppSettingsDocument>? Changed;

    public async Task<AppSettingsDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_current is not null)
        {
            return _current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadOrMigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Loads the document, migrating it if needed. Must be called under
    /// the lock.
    ///
    /// This is the only loading path: a write that bypassed the migration
    /// would stamp the file at the current version without having
    /// converted it, and the migration would then be lost forever.
    /// </summary>
    private async Task<AppSettingsDocument> LoadOrMigrateAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            return _current;
        }

        _current = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (Migrate(_current))
        {
            await _store.SaveAsync(_current, cancellationToken).ConfigureAwait(false);
        }

        return _current;
    }

    /// <summary>
    /// Updates a file written by an earlier version. Returns true if it
    /// was modified and must be rewritten.
    /// </summary>
    /// <remarks>
    /// Version 3: the virtual display switches to landscape. The game
    /// renders in landscape, and a portrait display used to shrink it to
    /// a strip in the middle of the window. Only the original resolution
    /// is corrected: a setting chosen by the user is respected.
    ///
    /// Version 4: the first size shrinks, device order is derived from
    /// instance order, and ranks are compacted. They used to have gaps,
    /// since they were never renumbered after a missed device, and two
    /// instances could carry the same rank. The checked boxes are not
    /// touched: they remain the startup set until the first exit through
    /// the Quit button, which rewrites them.
    /// </remarks>
    private static bool Migrate(AppSettingsDocument settings)
    {
        var changed = false;

        if (settings.SchemaVersion < 3
            && settings is { VirtualDisplayWidth: 1080, VirtualDisplayHeight: 1920, VirtualDisplayDpi: 320 })
        {
            settings.VirtualDisplayWidth = 1920;
            settings.VirtualDisplayHeight = 1080;
            settings.VirtualDisplayDpi = 240;
            changed = true;
        }

        if (settings.SchemaVersion < 4)
        {
            if (settings.SizePercentages.SequenceEqual(LegacySizePercentages))
            {
                settings.SizePercentages = [.. AppSettingsDocument.DefaultSizePercentages];
            }

            InstanceOrdering.Normalize(settings);
            changed = true;
        }

        if (settings.SchemaVersion < 5)
        {
            // The "free width" mode is removed. It could not keep its
            // promise: the game locks its layout height at startup, so
            // changing a window's height would crop the image or leave
            // a strip. The setting disappears from the file on rewrite,
            // with nothing left to decide.
            changed = true;
        }

        if (settings.SchemaVersion < 6)
        {
            // Devices are no longer sorted: order is global and free,
            // carried solely by each instance's rank. The device list
            // disappears from the file on rewrite, with nothing to
            // decide, since the ranks already carry the desired order.
            InstanceOrdering.Normalize(settings);
            changed = true;
        }

        if (settings.SchemaVersion < 7)
        {
            // Display density becomes a zoom setting, computed from the
            // chosen resolution. The fixed value in the file no longer
            // has any effect and disappears on rewrite; zoom starts at
            // the original setting, which gives the same result as
            // before.
            settings.GameZoom = GameZoom.Normal;
            changed = true;
        }

        if (settings.SchemaVersion < 10)
        {
            // Accounts gain a colour. **This one is a backfill, not an
            // enum-tolerance block**, which is what makes it different
            // from the two dead ones described just below: reading the
            // new field needs nothing, since a file that predates it
            // simply lacks the property and the value stays null. But
            // leaving every existing account without a colour would show
            // the feature as though it were broken, on exactly the
            // installations that have accounts to tell apart.
            //
            // In rank order, so the marks follow the list as it reads.
            // It runs once, so an account whose colour is later cleared
            // by hand stays cleared.
            foreach (var instance in settings.Instances.OrderBy(i => i.Order))
            {
                instance.Colour ??= AccountColours.NextFree(
                    settings.Instances.Select(i => i.Colour));
            }

            changed = true;
        }

        // Removed tiers, eighth and ninth versions: the "High" quality
        // merged into the maximum, the "very close" zoom merged into
        // "close". There is nothing to do here, and there must not be:
        // the lenient converter has already replaced the unknown value
        // on read, with the fallback declared on the enum, so an
        // Enum.IsDefined placed here is always true and the branch
        // never fired. Two dead migrations, one of which lied: a file
        // carrying "Closest" used to fall back to the original setting
        // and not to "close". The two fallbacks now carry the decision.

        if (settings.SchemaVersion != AppSettingsDocument.CurrentSchemaVersion)
        {
            settings.SchemaVersion = AppSettingsDocument.CurrentSchemaVersion;
            changed = true;
        }

        return changed;
    }

    /// <summary>Modifies the settings and writes them.</summary>
    public async Task UpdateAsync(
        Action<AppSettingsDocument> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AppSettingsDocument document;

        try
        {
            document = await LoadOrMigrateAsync(cancellationToken).ConfigureAwait(false);
            mutate(document);

            // The stamp is set by the migration, and only by it:
            // setting it here would mark as up to date a document
            // that is not.
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, document);
    }

    /// <summary>
    /// Modifies the settings and writes them only if something changed.
    ///
    /// Reasserting a state already in place, which is what every
    /// startup does, used to rewrite the file and notify everyone for
    /// nothing.
    /// </summary>
    /// <returns>True if the file was rewritten.</returns>
    public async Task<bool> UpdateIfChangedAsync(
        Func<AppSettingsDocument, bool> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AppSettingsDocument document;

        try
        {
            document = await LoadOrMigrateAsync(cancellationToken).ConfigureAwait(false);

            if (!mutate(document))
            {
                return false;
            }

            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, document);

        return true;
    }

    /// <summary>Forces a reread from disk on the next access.</summary>
    public void Invalidate() => _current = null;

    /// <summary>Mirroring settings derived from the preferences.</summary>
    public async Task<ScrcpyOptions> GetScrcpyOptionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        var profile = QualityProfile.For(settings.Quality, settings.CustomQuality);

        return new ScrcpyOptions
        {
            AudioEnabled = settings.AudioEnabled,
            ClipboardSyncEnabled = settings.ClipboardSyncEnabled,

            // The simulated physical keyboard bypasses the device's
            // virtual keyboard, which swallows characters across
            // several layers.
            KeyboardMode = settings.SimulatedPhysicalKeyboard
                ? ScrcpyKeyboardMode.Uhid
                : ScrcpyKeyboardMode.Sdk,
            MouseMode = settings.SimulatedPhysicalMouse
                ? ScrcpyMouseMode.Uhid
                : ScrcpyMouseMode.Sdk,
            VirtualDisplayWidth = settings.VirtualDisplayWidth,
            VirtualDisplayHeight = settings.VirtualDisplayHeight,
            VirtualDisplayDpi = settings.VirtualDisplayDpi,

            // The codec is only chosen at the custom tier. Elsewhere
            // it stays null, and scrcpy decides: it is the one that
            // knows what the device encodes in hardware.
            VideoCodec = settings.Quality == StreamQuality.Custom
                ? settings.CustomQuality.Sanitized().VideoCodec
                : null,
            // Frames per second and bitrate come only from the chosen
            // quality: two sources for the same setting would have
            // ended up diverging.
            //
            // The bitrate here is the one for the stored resolution. It
            // is recalculated at launch on the resolution actually
            // used, which follows the window size: that is where it
            // takes on its meaning.
            MaxFps = profile.MaxFps,
            VideoBitrateKbps = profile.BitrateFor(
                settings.VirtualDisplayWidth,
                Math.Min(settings.VirtualDisplayHeight, profile.MaximumDisplayHeight)),
        }.Sanitized();
    }

    /// <summary>Remembers the chosen quality.</summary>
    public Task SetQualityAsync(StreamQuality quality, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.Quality = quality, cancellationToken);

    /// <summary>Remembers the values of the custom tier.</summary>
    public Task SetCustomQualityAsync(
        CustomQuality custom,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(custom);

        return UpdateAsync(settings => settings.CustomQuality = custom.Sanitized(), cancellationToken);
    }

    /// <summary>
    /// Remembers whether the phone's sound should play on the PC.
    /// </summary>
    public Task SetAudioEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.AudioEnabled = enabled, cancellationToken);

    /// <summary>Remembers the chosen keyboard mode.</summary>
    public Task SetSimulatedPhysicalKeyboardAsync(
        bool simulated,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.SimulatedPhysicalKeyboard = simulated, cancellationToken);

    /// <summary>Remembers the chosen mouse mode.</summary>
    public Task SetSimulatedPhysicalMouseAsync(
        bool simulated,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.SimulatedPhysicalMouse = simulated, cancellationToken);

    /// <summary>Remembers the chosen apparent distance.</summary>
    public Task SetZoomAsync(GameZoom zoom, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.GameZoom = zoom, cancellationToken);

    // Named sessions

    /// <summary>
    /// The saved profiles, in the order they were created.
    /// </summary>
    public async Task<IReadOnlyList<StoredLaunchProfile>> GetLaunchProfilesAsync(
        CancellationToken cancellationToken = default) =>
        [.. (await GetAsync(cancellationToken).ConfigureAwait(false)).LaunchProfiles];

    /// <summary>
    /// The name under which the tabbed frame's placement is remembered.
    ///
    /// The same key as the one the UI service uses to save it: the
    /// profiles touch it too, and two spellings would have produced two
    /// entries, only one of which would have ever been used.
    /// </summary>
    public const string TabsPlacementKey = "tabs";

    /// <summary>
    /// Name of the profile opened at startup, or <c>null</c> if there
    /// is none.
    /// </summary>
    public async Task<string?> GetDefaultLaunchProfileAsync(
        CancellationToken cancellationToken = default) =>
        LaunchProfiles.Normalize(
            (await GetAsync(cancellationToken).ConfigureAwait(false)).DefaultLaunchProfile);

    /// <summary>
    /// Remembers a session under this name, replacing the one that
    /// held it.
    ///
    /// Replacing rather than refusing: saving twice under the same
    /// name is the natural gesture to fix a profile, and returning an
    /// error would force deleting it first.
    /// </summary>
    /// <returns>False if the name is not one.</returns>
    public async Task<bool> SaveLaunchProfileAsync(
        string? name,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (LaunchProfiles.Normalize(name) is not { } wanted)
        {
            return false;
        }

        await UpdateAsync(
            settings =>
            {
                var existing = LaunchProfiles.Find(settings.LaunchProfiles, wanted);

                if (existing is not null)
                {
                    settings.LaunchProfiles.Remove(existing);
                }

                // In the order the accounts are arranged, not the one
                // the caller gives them: this is the order the list
                // shows and the tabs follow, so it is the one the
                // profile must restore when it opens.
                var voulus = keys.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

                var retenus = settings.Instances
                    .Where(i => voulus.Contains(i.Key))
                    .OrderBy(i => i.Order)
                    .Select(i => i.Key)
                    .ToList();

                // An account named by the caller that the document
                // does not know keeps its place: a profile is better
                // trimmed than refused.
                retenus.AddRange(voulus.Where(k => !retenus.Contains(k, StringComparer.Ordinal)));

                // The snapshot is taken from the document itself: the
                // geometry is already there, recorded just before by
                // the caller, and the settings live there permanently.
                // Passing them as parameters would have opened the
                // door to a profile that remembers something other
                // than what the screen shows.
                settings.LaunchProfiles.Add(new StoredLaunchProfile
                {
                    Name = wanted,
                    InstanceKeys = retenus,
                    Windows = settings.Instances
                        .Where(i => retenus.Contains(i.Key, StringComparer.Ordinal)
                                    && i.Window is not null)
                        .ToDictionary(i => i.Key, i => i.Window!, StringComparer.Ordinal),
                    Quality = settings.Quality,
                    CustomQuality = settings.CustomQuality.Sanitized(),
                    GameZoom = settings.GameZoom,
                    GameAnchor = settings.GameAnchor,
                    SizeIndex = settings.SizeIndex,
                    CustomSizePercent = settings.CustomSizePercent,
                    AudioEnabled = settings.AudioEnabled,
                    ClipboardSyncEnabled = settings.ClipboardSyncEnabled,
                    TabbedKeys = [.. settings.Instances
                        .Where(i => i.IsTabbed && retenus.Contains(i.Key, StringComparer.Ordinal))
                        .OrderBy(i => i.Order)
                        .Select(i => i.Key)],

                    // The frame's placement is only remembered if the
                    // profile houses something: keeping it on a profile
                    // with no tabs would move another profile's frame
                    // when it opens.
                    TabsWindow = settings.Instances.Any(
                        i => i.IsTabbed && retenus.Contains(i.Key, StringComparer.Ordinal))
                        ? settings.WindowPlacements.GetValueOrDefault(TabsPlacementKey)
                        : null,
                });
            },
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Removes a profile, and the startup default with it if it
    /// pointed to it.
    ///
    /// Leaving a default that points to nothing would give a startup
    /// that does not open what is expected, with nothing to explain
    /// it.
    /// </summary>
    public Task DeleteLaunchProfileAsync(string? name, CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                if (LaunchProfiles.Find(settings.LaunchProfiles, name) is not { } profile)
                {
                    return false;
                }

                settings.LaunchProfiles.Remove(profile);

                if (LaunchProfiles.Find([profile], settings.DefaultLaunchProfile) is not null)
                {
                    settings.DefaultLaunchProfile = string.Empty;
                }

                return true;
            },
            cancellationToken);

    /// <summary>
    /// Sets the startup profile. <c>null</c> resets it to "none", and
    /// the application then reopens whatever was open, as before.
    /// </summary>
    public Task SetDefaultLaunchProfileAsync(
        string? name,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                var wanted = LaunchProfiles.Find(settings.LaunchProfiles, name)?.Name
                    ?? string.Empty;

                if (string.Equals(settings.DefaultLaunchProfile, wanted, StringComparison.Ordinal))
                {
                    return false;
                }

                settings.DefaultLaunchProfile = wanted;
                return true;
            },
            cancellationToken);

    /// <summary>
    /// Makes this profile the startup set: its accounts are checked,
    /// the others unchecked.
    /// </summary>
    /// <returns>
    /// The profile's accounts, or an empty list if it does not exist.
    /// </returns>
    public async Task<IReadOnlyList<string>> ApplyLaunchProfileAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        if (LaunchProfiles.Find(settings.LaunchProfiles, name) is null)
        {
            return [];
        }

        var wanted = LaunchProfiles.KeysFor(settings.LaunchProfiles, name, settings.Instances);

        var profile = LaunchProfiles.Find(settings.LaunchProfiles, name)!;

        // A single write, not three: successive calls would notify
        // every time, and the list would refresh on an intermediate
        // state where the accounts are set but the positions are not
        // yet.
        await UpdateAsync(
            document =>
            {
                var keys = wanted.ToHashSet(StringComparer.Ordinal);

                var housed = profile.TabbedKeys.ToHashSet(StringComparer.Ordinal);

                foreach (var instance in document.Instances)
                {
                    instance.IsEnabled = keys.Contains(instance.Key);

                    // A profile from before tabbed mode names none of
                    // them: its accounts then keep their mode,
                    // otherwise it would take them all out of the
                    // frame without being asked to.
                    if (profile.TabbedKeys.Count > 0)
                    {
                        instance.IsTabbed = housed.Contains(instance.Key);
                    }

                    // The profile's position wins. An account the
                    // profile does not place keeps its own: a profile
                    // from before positions must not send everything
                    // back to the anchor.
                    if (profile.Windows.TryGetValue(instance.Key, out var rect))
                    {
                        instance.Window = rect;
                    }
                }

                // The account order is NOT restored by the profile,
                // and that is deliberate: it is a general setting,
                // shared by the list and by the tabs. A profile that
                // replayed it would undo the mouse arrangement as soon
                // as the next startup, with the startup profile
                // opening on its own. The profile remembers it to stay
                // readable; it does not impose it.

                document.Quality = profile.Quality;
                document.CustomQuality = profile.CustomQuality.Sanitized();
                document.GameZoom = profile.GameZoom;
                document.GameAnchor = profile.GameAnchor;
                document.AudioEnabled = profile.AudioEnabled;
                document.ClipboardSyncEnabled = profile.ClipboardSyncEnabled;

                // A profile with no frame placement leaves the one
                // that is remembered: this is the case for profiles
                // saved before, and for those that house nothing.
                if (profile.TabsWindow is { IsSized: true } cadre)
                {
                    document.WindowPlacements[TabsPlacementKey] = cadre;
                }

                document.SizeIndex = profile.SizeIndex;
                document.CustomSizePercent = profile.CustomSizePercent;
            },
            cancellationToken).ConfigureAwait(false);

        return wanted;
    }

    /// <summary>Moves an account into or out of the tabbed frame.</summary>
    public Task SetInstanceTabbedAsync(
        string key,
        bool tabbed,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                if (settings.Instances.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal))
                    is not { } instance || instance.IsTabbed == tabbed)
                {
                    return false;
                }

                instance.IsTabbed = tabbed;
                return true;
            },
            cancellationToken);

    /// <summary>
    /// Each account's quality profile: its own if it chose one, the
    /// shared one otherwise.
    ///
    /// Resolved here rather than at the caller: this is the only place
    /// that sees both the accounts and the shared setting, and the
    /// precedence rule does not need to be repeated elsewhere.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, QualityProfile>> GetInstanceQualitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<string, QualityProfile> profiles = new(StringComparer.Ordinal);

        foreach (var instance in settings.Instances)
        {
            profiles[instance.Key] = InstanceQuality.ProfileFor(
                instance.Quality,
                settings.Quality,
                settings.CustomQuality);
        }

        return profiles;
    }

    /// <summary>
    /// Gives an account its own tier, or returns it to the shared
    /// setting with <c>null</c>.
    /// </summary>
    public Task SetInstanceQualityAsync(
        string key,
        StreamQuality? quality,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                var instance = settings.Instances
                    .FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is null || instance.Quality == quality)
                {
                    return false;
                }

                instance.Quality = quality;

                return true;
            },
            cancellationToken);

    /// <summary>
    /// The distance that applies to each account, its own if it has
    /// one, otherwise the shared one.
    ///
    /// Same shape as <see cref="GetInstanceQualitiesAsync"/>, and for
    /// the same reason: the rule is resolved here, once, and the
    /// launcher only has to read it. There is no third source, a
    /// launch profile copying its values into the shared setting
    /// before launch.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, GameZoom>> GetInstanceZoomsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<string, GameZoom> zooms = new(StringComparer.Ordinal);

        foreach (var instance in settings.Instances)
        {
            zooms[instance.Key] = instance.GameZoom ?? settings.GameZoom;
        }

        return zooms;
    }

    /// <summary>
    /// Gives an account its own distance, or returns it to the shared
    /// setting with <c>null</c>.
    /// </summary>
    public Task SetInstanceZoomAsync(
        string key,
        GameZoom? zoom,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                var instance = settings.Instances
                    .FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is null || instance.GameZoom == zoom)
                {
                    return false;
                }

                instance.GameZoom = zoom;

                return true;
            },
            cancellationToken);

    /// <summary>
    /// Gives an account a colour, or takes it away with <c>null</c>.
    ///
    /// Through the change-checking write, like the tier and the
    /// distance: re-asserting the same colour at startup must not
    /// rewrite the file and wake every subscriber.
    /// </summary>
    public Task SetInstanceColourAsync(
        string key,
        AccountColour? colour,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                var instance = settings.Instances
                    .FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is null || instance.Colour == colour)
                {
                    return false;
                }

                instance.Colour = colour;

                return true;
            },
            cancellationToken);

    /// <summary>
    /// Adds playtime to the account, for today.
    ///
    /// Nothing is written for a session of a handful of seconds:
    /// opening and immediately closing again is not playtime, and
    /// writing it would make a file write for nothing.
    /// </summary>
    public Task AddPlaytimeAsync(
        string key,
        int seconds,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                if (seconds < MinimumCountedPlaytimeSeconds)
                {
                    return false;
                }

                var instance = settings.Instances
                    .FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is null)
                {
                    return false;
                }

                instance.Playtime = new Dictionary<string, int>(
                    PlaytimeLog.Add(instance.Playtime, DateOnly.FromDateTime(DateTime.Now), seconds),
                    StringComparer.Ordinal);

                return true;
            },
            cancellationToken);

    /// <summary>Below this, the session does not count as playtime.</summary>
    public const int MinimumCountedPlaytimeSeconds = 30;

    /// <summary>
    /// The settings, exactly as they are carried out: the file
    /// itself.
    ///
    /// The file, not a recomposed excerpt: what gets read back is
    /// exactly what was written, and a separate export format would be
    /// a second format to maintain, which would eventually diverge
    /// from the first.
    /// </summary>
    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return _store.Serialize(settings);
    }

    /// <summary>
    /// Replaces the settings with those from a file, if it is
    /// readable.
    ///
    /// Nothing is applied until the inspection has concluded: a file
    /// from a newer version would be rolled back through the ordinary
    /// path, and the user would believe the restore had worked. See
    /// <see cref="SettingsBackup" />.
    /// </summary>
    public async Task<BackupVerdict> ImportAsync(
        string? json,
        CancellationToken cancellationToken = default)
    {
        var inspection = SettingsBackup.Inspect(json);

        if (inspection.Verdict != BackupVerdict.Usable)
        {
            return inspection.Verdict;
        }

        if (_store.Deserialize(json!) is not { } incoming)
        {
            // The inspection said the shape held; a wrongly typed
            // field can still make the full read fail. We refuse
            // rather than apply it halfway.
            return BackupVerdict.Unreadable;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Migrated like any other file: an older export is
            // entitled to the same conversions as a local file.
            _ = Migrate(incoming);

            _current = incoming;

            await _store.SaveAsync(incoming, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, incoming);

        return BackupVerdict.Usable;
    }

    /// <summary>
    /// Configured sizes, corrected if the file is inconsistent.
    /// </summary>
    public async Task<WindowSizePresets> GetSizePresetsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return new WindowSizePresets { Percentages = settings.SizePercentages }.Sanitized();
    }

    /// <summary>
    /// Configured hotkeys, repaired if the file is inconsistent.
    /// </summary>
    public async Task<HotkeySet> GetHotkeysAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        var bindings = settings.Hotkeys
            .Select(h => h.ToBinding())
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        return HotkeySet.FromBindings(bindings.Count == 0 ? null : bindings);
    }

    public Task SaveHotkeysAsync(HotkeySet hotkeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        return UpdateAsync(
            settings => settings.Hotkeys = [.. hotkeys.Bindings.Select(StoredHotkey.From)],
            cancellationToken);
    }

    /// <summary>
    /// Forgets instances whose Android profile no longer exists.
    ///
    /// A remembered instance survives a disconnection, and that is
    /// deliberate: an unplugged phone must keep its rows. It also used
    /// to survive the profile's deletion, which left in the list an
    /// account that exists nowhere, that no button could remove.
    ///
    /// We do not rely on the game's absence, which could be just a
    /// passing command failure: we rely on the profile's disappearance.
    /// Only phones whose profile list was actually read are concerned;
    /// the others prove nothing.
    /// </summary>
    /// <param name="profiles">
    /// Profiles collected, by device identifier.
    /// </param>
    /// <param name="withoutGame">
    /// Profiles that responded and do not have the game, by device
    /// identifier. Only those count: a profile that failed to respond
    /// does not appear here, and its silence proves nothing.
    /// </param>
    /// <returns>The number of instances forgotten.</returns>
    public async Task<int> ForgetMissingProfilesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<int>> profiles,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? withoutGame = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return 0;
        }

        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        // Two reasons to forget, and a single shared guard: the phone
        // must have responded. Either the profile has disappeared from
        // the list, or it is still there and has itself said it no
        // longer has the game. Uninstalling the game from a profile we
        // keep would otherwise leave a ghost account in the list,
        // indefinitely and even across restarts.
        var gone = settings.Instances
            .Where(i => profiles.TryGetValue(i.DeviceId, out var live) && !live.Contains(i.UserId))
            .Select(i => i.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (withoutGame is not null)
        {
            foreach (var instance in settings.Instances)
            {
                if (withoutGame.TryGetValue(instance.DeviceId, out var empty)
                    && empty.Contains(instance.UserId))
                {
                    _ = gone.Add(instance.Key);
                }
            }
        }

        // Nothing to remove: we do not write. A write with no change
        // on every sweep would wear out the file for nothing.
        if (gone.Count == 0)
        {
            return 0;
        }

        await UpdateAsync(
            document =>
            {
                // The window's geometry leaves with the instance: it
                // is carried by the entry itself, not by the placement
                // dictionary, which only knows our own windows.
                _ = document.Instances.RemoveAll(i => gone.Contains(i.Key));

                InstanceOrdering.Normalize(document);
            },
            cancellationToken).ConfigureAwait(false);

        return gone.Count;
    }

    /// <summary>
    /// Merges the discovered instances with those that were
    /// remembered. The name chosen by the user and the launch checkbox
    /// belong to it: a rediscovery never overwrites them.
    /// </summary>
    public async Task<IReadOnlyList<DofusInstance>> MergeInstancesAsync(
        IReadOnlyList<DofusInstance> discovered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        IReadOnlyList<DofusInstance> merged = [];

        await UpdateAsync(settings =>
        {
            var stored = settings.Instances.ToDictionary(i => i.Key, StringComparer.Ordinal);

            foreach (var instance in discovered)
            {
                if (stored.TryGetValue(instance.Key, out var existing))
                {
                    existing.DeviceName = instance.DeviceName;
                    existing.UserName = instance.UserName;
                    existing.LaunchComponent = instance.LaunchComponent ?? existing.LaunchComponent;

                    // An account remembered from before colours existed,
                    // or one that slipped past the one-shot backfill,
                    // would otherwise stay unmarked for good. Only a
                    // null is filled: None is a decision and is left
                    // alone.
                    existing.Colour ??= AccountColours.NextFree(
                        settings.Instances.Select(i => i.Colour));

                    continue;
                }

                var entry = new StoredInstance
                {
                    DeviceId = instance.DeviceId,
                    UserId = instance.UserId,
                    PackageName = instance.PackageName,
                    DeviceName = instance.DeviceName,
                    UserName = instance.UserName,
                    LaunchComponent = instance.LaunchComponent,
                    IsEnabled = false,

                    // Computed before the ordering inserts the entry, or
                    // it would see itself and skip its own turn.
                    //
                    // Unlike the tier and the distance, which default to
                    // null because a shared setting already works, a
                    // colourless account gives nothing at all: a feature
                    // whose point is that you never have to think about
                    // which window is which cannot ask for six decisions
                    // first.
                    Colour = AccountColours.NextFree(settings.Instances.Select(i => i.Colour)),
                };

                // Right after those of its device: a new instance must
                // appear near its siblings, not at the end of a long
                // list where it would go unseen.
                InstanceOrdering.Add(settings, entry);
                stored[entry.Key] = entry;
            }

            InstanceOrdering.Normalize(settings);

            var live = discovered.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);

            merged = [.. settings.Instances
                .OrderBy(i => i.Order)
                .Select(i => new DofusInstance
                {
                    DeviceId = i.DeviceId,
                    DeviceName = i.DeviceName,
                    UserId = i.UserId,
                    UserName = i.UserName,
                    PackageName = i.PackageName,
                    LaunchComponent = i.LaunchComponent,
                    CustomName = i.CustomName,
                    IsEnabled = i.IsEnabled,
                    IsManaged = i.IsManaged,
                    IsTabbed = i.IsTabbed,
                    Quality = i.Quality,
                    Zoom = i.GameZoom,
                    Colour = i.Colour,
                    PlayedThisWeek = PlaytimeLog.Week(i.Playtime, DateOnly.FromDateTime(DateTime.Now)),
                    IsDeviceConnected = live.Contains(i.Key),
                })];
        }, cancellationToken).ConfigureAwait(false);

        return merged;
    }

    /// <summary>
    /// Checks or unchecks an instance for automatic launch.
    /// </summary>
    public Task SetInstanceEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
        {
            var instance = settings.Instances.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal));
            if (instance is not null)
            {
                instance.IsEnabled = enabled;
            }
        }, cancellationToken);

    /// <summary>
    /// Renames an instance. An empty name restores the Android
    /// profile's name.
    /// </summary>
    public Task RenameInstanceAsync(string key, string? name, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
        {
            var instance = settings.Instances.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal));
            if (instance is not null)
            {
                instance.CustomName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
        }, cancellationToken);

    /// <summary>
    /// Forgets the instances of a removed phone, and its rank.
    /// </summary>
    public Task ForgetDeviceAsync(string deviceId, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
        {
            settings.Instances.RemoveAll(i => string.Equals(i.DeviceId, deviceId, StringComparison.Ordinal));

            // The remembered geometry leaves with the instances: it is
            // nested inside them. What remains is the ranks, which
            // must be compacted.
            InstanceOrdering.Normalize(settings);
        }, cancellationToken);

    // Window geometry

    /// <summary>Remembered geometries, by instance key.</summary>
    public async Task<IReadOnlyDictionary<string, StoredWindowRect>> GetWindowRectsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return settings.Instances
            .Where(i => i.Window is not null)
            .ToDictionary(i => i.Key, i => i.Window!, StringComparer.Ordinal);
    }

    /// <summary>
    /// Saves several geometries in a single write. Instances absent
    /// from the dictionary keep their own: a window that was not open
    /// must not lose the place where it had been left.
    /// </summary>
    public Task SaveWindowRectsAsync(
        IReadOnlyDictionary<string, StoredWindowRect> rects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rects);

        return UpdateAsync(settings =>
        {
            foreach (var (key, rect) in rects)
            {
                var instance = settings.Instances.Find(
                    i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is not null)
                {
                    instance.Window = rect;
                }
            }
        }, cancellationToken);
    }

    // Order

    /// <summary>
    /// Rank of each instance, by its key, to sort sessions.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetInstanceRanksAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return settings.Instances.ToDictionary(i => i.Key, i => i.Order, StringComparer.Ordinal);
    }

    /// <summary>
    /// Places an instance just before or just after another,
    /// regardless of their device. Returns false if nothing moves.
    /// </summary>
    public async Task<bool> MoveInstanceAsync(
        string key,
        string targetKey,
        bool above,
        CancellationToken cancellationToken = default)
    {
        var moved = false;

        await UpdateAsync(
            settings => moved = InstanceOrdering.MoveInstance(settings, key, targetKey, above),
            cancellationToken).ConfigureAwait(false);

        return moved;
    }

    // Startup

    /// <summary>
    /// Marks instances as part of the next launch, or removes them
    /// from it.
    ///
    /// Launching an instance puts it in, closing it through the button
    /// takes it out, and nothing else touches it: closing a game
    /// window by hand, quitting the application, or losing the phone
    /// all leave the set intact.
    /// </summary>
    public async Task SetInstancesEnabledAsync(
        IReadOnlyCollection<string> keys,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return;
        }

        var wanted = keys.ToHashSet(StringComparer.Ordinal);

        await UpdateIfChangedAsync(
            settings =>
            {
                var changed = false;

                foreach (var instance in settings.Instances.Where(i => wanted.Contains(i.Key)))
                {
                    if (instance.IsEnabled != enabled)
                    {
                        instance.IsEnabled = enabled;
                        changed = true;
                    }
                }

                return changed;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Remembers whether a window follows automatic placements.
    /// </summary>
    public Task SetInstanceManagedAsync(
        string key,
        bool managed,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                var instance = settings.Instances.Find(
                    i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is null || instance.IsManaged == managed)
                {
                    return false;
                }

                instance.IsManaged = managed;

                return true;
            },
            cancellationToken);

    /// <summary>Instances left aside by automatic placements.</summary>
    public async Task<IReadOnlySet<string>> GetUnmanagedKeysAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return settings.Instances
            .Where(i => !i.IsManaged)
            .Select(i => i.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Remembers the size set at the cursor.</summary>
    public Task SaveCustomSizePercentAsync(int percent, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.CustomSizePercent = Math.Clamp(percent, 0, 100), cancellationToken);

    /// <summary>
    /// Remembers whether the configurator was shown on exit.
    /// </summary>
    public Task SetConfiguratorVisibleAsync(bool visible, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.ConfiguratorVisible = visible, cancellationToken);

    /// <summary>
    /// Remembers whether the quest tracker was open and on which
    /// quest, to reopen it exactly the same way on the next launch.
    /// </summary>
    public Task SetQuestsStateAsync(
        bool visible,
        string? lastQuestUrl,
        int lastQuestStep = 0,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            settings =>
            {
                settings.QuestsVisible = visible;

                // The step follows the address: remembering a rank
                // without the guide it belongs to would reopen a
                // different quest at a step that is not its own.
                if (!string.IsNullOrWhiteSpace(lastQuestUrl))
                {
                    settings.LastQuestStep = lastQuestStep < 0 ? 0 : lastQuestStep;
                }

                // An empty address does not erase the previous one:
                // closing the window on its list must not make it
                // forget the quest that was being read there before.
                if (!string.IsNullOrWhiteSpace(lastQuestUrl))
                {
                    settings.LastQuestUrl = lastQuestUrl;
                }
            },
            cancellationToken);

    /// <summary>
    /// Remembers where a window was. A placement with no size is not
    /// saved: that is what a window that was never shown returns.
    /// </summary>
    public Task SetWindowPlacementAsync(
        string key,
        WindowPlacement? placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (placement is not { IsSized: true })
        {
            return Task.CompletedTask;
        }

        return UpdateAsync(settings => settings.WindowPlacements[key] = placement, cancellationToken);
    }

    public void Dispose() => _gate.Dispose();
}

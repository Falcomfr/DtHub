using System.Text.Json.Serialization;

using DtHub.Core.Hotkeys;
using DtHub.Core.Windows;

namespace DtHub.Core.Settings;

/// <summary>
/// Persisted shape of <c>settings.json</c>. The default values are viable:
/// a missing, partial, or hand-edited file must still produce a usable
/// configuration.
/// </summary>
public sealed class AppSettingsDocument
{
    public const int CurrentSchemaVersion = 9;

    /// <summary>
    /// Default shipped sizes, as a percentage of the usable area.
    /// </summary>
    public static readonly int[] DefaultSizePercentages = [40, 60, 80, 100];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Stored instances, including those that are not checked.
    /// </summary>
    public List<StoredInstance> Instances { get; set; } = [];

    /// <summary>
    /// Named sessions: groups of accounts opened with a single gesture.
    ///
    /// Kept apart from the startup set, and that is the whole point: the
    /// startup set reshapes itself with every gesture, a launch adding the
    /// opened accounts to it and the "close" button removing them. A
    /// profile derived from it would keep rewriting itself.
    /// </summary>
    public List<StoredLaunchProfile> LaunchProfiles { get; set; } = [];

    /// <summary>
    /// Profile opened at startup. Empty: none, and what was open last time
    /// reopens instead, as the application has always done.
    /// </summary>
    public string DefaultLaunchProfile { get; set; } = string.Empty;

    // Windows

    /// <summary>Position of the block of game windows on the screen.</summary>
    public WindowAnchor GameAnchor { get; set; } = WindowAnchor.MiddleLeft;

    /// <summary>
    /// Proposed sizes, as a percentage of the screen's usable area. They
    /// are therefore proportional to the screen in use.
    /// </summary>
    public List<int> SizePercentages { get; set; } = [.. DefaultSizePercentages];

    /// <summary>
    /// Chosen size, by its index. The last one is full screen.
    /// </summary>
    public int SizeIndex { get; set; } = 1;

    /// <summary>
    /// Size set by the cursor, in percent. Zero when a hotkey decided it,
    /// in which case the index is authoritative.
    /// </summary>
    public int CustomSizePercent { get; set; }

    /// <summary>
    /// True if the configurator was shown on exit. It restores that state
    /// on the next launch.
    /// </summary>
    public bool ConfiguratorVisible { get; set; } = true;

    /// <summary>
    /// True if the quest tracker was shown on exit. It then reopens on
    /// the next launch, on the last quest that was read.
    /// </summary>
    public bool QuestsVisible { get; set; }

    /// <summary>
    /// The last quest opened in the tracker. Empty until one has been, in
    /// which case the window reopens on its list.
    /// </summary>
    public string LastQuestUrl { get; set; } = string.Empty;

    /// <summary>
    /// The step reached in that guide. Only the address used to be kept:
    /// the right guide reopened at its first step, and the path had to be
    /// walked again. Zero means "the first one", which is also the
    /// fallback when the page has fewer steps than before.
    /// </summary>
    public int LastQuestStep { get; set; }

    /// <summary>
    /// True when the application updates itself: it downloads the release
    /// in the background and installs it on exit, never in the middle of
    /// a session. Unchecked, it only reports that a new version exists.
    /// </summary>
    public bool UpdatesAutomatic { get; set; } = true;

    /// <summary>
    /// The interface language, as two letters. Empty to follow Windows's
    /// display language, which is the ordinary case: this setting exists
    /// only to override it.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Where the application's windows are, by name.
    ///
    /// A table rather than one field per window: they all look alike on
    /// this point, and a new one then has nothing to add here.
    /// </summary>
    public Dictionary<string, WindowPlacement> WindowPlacements { get; set; } = [];

    // Mirroring

    public bool AudioEnabled { get; set; }
    public bool ClipboardSyncEnabled { get; set; } = true;

    /// <summary>
    /// True if closing a game window also stops the game on the phone.
    ///
    /// Without this the game survives its window, indefinitely: a closed
    /// account kept two hundred and twenty megabytes and its connection to
    /// the game's servers, and forgotten ones piled up from one launch to
    /// the next.
    ///
    /// True by default. False restores the previous behavior, which has
    /// its merit: a window closed and reopened finds the character still
    /// in game, with no reconnection.
    ///
    /// Deliberately absent from <see cref="StoredLaunchProfile" />: a named
    /// session describes a game environment, not the habits of the person
    /// using it, and a profile that reimposed this choice on every launch
    /// would be exactly the trap already met with quality.
    /// </summary>
    public bool StopAppOnClose { get; set; } = true;

    /// <summary>
    /// True if the keyboard is presented to the phone as a physical
    /// keyboard plugged in, rather than injected through the Android API.
    ///
    /// Injection through the API goes by way of the device's virtual
    /// keyboard, and several vendor overlays supply one that swallows
    /// characters: the window responds to the mouse but nothing gets
    /// typed. A simulated physical keyboard bypasses it entirely.
    ///
    /// False by default, for a reason that is not obvious: a physical
    /// keyboard is interpreted according to the layout set in Android. If
    /// it does not match the PC's, an AZERTY types as QWERTY. The remedy
    /// must therefore not be forced on those who do not have the problem.
    ///
    /// Deliberately absent from <see cref="StoredLaunchProfile" />, like
    /// stopping the game on close: it is a habit of the person playing,
    /// not a description of their game environment.
    /// </summary>
    public bool SimulatedPhysicalKeyboard { get; set; }

    /// <summary>
    /// Mouse presented to the phone as a mouse plugged in.
    ///
    /// Last resort for a device whose overlay refuses injection: the
    /// simulated mouse does not go through it. **It captures the machine's
    /// cursor**, which makes it impractical with several windows, hence
    /// the default off state and the trade-off noted next to the checkbox.
    ///
    /// Absent from <see cref="StoredLaunchProfile" /> for the same reason
    /// as the keyboard: it is a habit of the person playing.
    /// </summary>
    public bool SimulatedPhysicalMouse { get; set; }

    /// <summary>
    /// Asks scrcpy to write its frame rate to the log, one line per
    /// second and per window.
    ///
    /// Off by default, and for diagnostics only: it is the answer to "it
    /// stutters", not a comfort setting. **Zero frames per second is not
    /// a fault**: scrcpy only encodes what changes, and a still screen
    /// produces nothing.
    /// </summary>
    public bool FluidityDiagnostics { get; set; }
    // The game displays in landscape: a vertical virtual display would
    // center it in 16:9 amid a tall window, with two wide black bars.
    public int VirtualDisplayWidth { get; set; } = 1920;
    public int VirtualDisplayHeight { get; set; } = 1080;
    public int VirtualDisplayDpi { get; set; } = 240;

    /// <summary>
    /// Trade-off between image sharpness and load on the machine. The
    /// medium value is the original one: nothing changes until it is
    /// touched.
    /// </summary>
    public StreamQuality Quality { get; set; } = StreamQuality.Medium;

    /// <summary>
    /// The values of the custom tier. They matter only when
    /// <see cref="Quality"/> is <see cref="StreamQuality.Custom"/>, but
    /// are kept even when another tier is chosen: whoever switches back
    /// to custom finds their settings again instead of retyping them all.
    /// </summary>
    public CustomQuality CustomQuality { get; set; } = new();

    /// <summary>
    /// Apparent distance in the game. Like quality, it is fixed when a
    /// session opens: changing it reopens the windows.
    /// </summary>
    public GameZoom GameZoom { get; set; } = GameZoom.Normal;

    /// <summary>
    /// Game package. Configurable to survive an upstream change.
    /// </summary>
    public string PackageName { get; set; } = Dofus.DofusPackages.DofusTouch;

    // Hotkeys
    public List<StoredHotkey> Hotkeys { get; set; } = [];
}

/// <summary>
/// A named session: a name, and the accounts it opens.
///
/// Accounts are designated by the key from
/// <see cref="StoredInstance.Key"/>, already stable from one launch to
/// the next and already used everywhere else. A key whose instance has
/// disappeared is simply ignored on opening: the profile keeps its
/// purpose, and the remaining accounts open.
/// </summary>
public sealed class StoredLaunchProfile
{
    public string Name { get; set; } = string.Empty;

    public List<string> InstanceKeys { get; set; } = [];

    /// <summary>
    /// Where each window sits, by instance key.
    ///
    /// This is what makes a profile more than a list of accounts: "solo
    /// dungeon" opens one window full size, "duo fishing" opens two side
    /// by side. Empty on a profile saved before profiles carried
    /// positions; its accounts then open wherever they were.
    /// </summary>
    public Dictionary<string, StoredWindowRect> Windows { get; set; } = [];

    /// <summary>
    /// The settings restored with the profile.
    ///
    /// They are fixed when scrcpy opens, so changing them requires
    /// reopening the windows. Switching from one profile to another
    /// reopens them anyway: so this costs nothing extra.
    /// </summary>
    public StreamQuality Quality { get; set; } = StreamQuality.Medium;

    public CustomQuality CustomQuality { get; set; } = new();

    public GameZoom GameZoom { get; set; } = GameZoom.Normal;

    /// <summary>
    /// Anchor and size: what automatic replacements rely on.
    /// </summary>
    public WindowAnchor GameAnchor { get; set; } = WindowAnchor.MiddleLeft;

    public int SizeIndex { get; set; } = 1;

    public int CustomSizePercent { get; set; }

    /// <summary>
    /// The game's sound sent back to the PC, and the clipboard shared
    /// with the phone.
    ///
    /// They are as much part of the environment as quality: play is not
    /// the same with and without sound, and a profile that did not keep
    /// them would not quite reproduce the same workspace.
    /// </summary>
    public bool AudioEnabled { get; set; }

    public bool ClipboardSyncEnabled { get; set; } = true;

    /// <summary>
    /// Where the tabbed frame was, when the profile used one.
    ///
    /// Without it, a tabbed profile would reopen its frame wherever
    /// Windows chose to put it: the positions kept for each account say
    /// nothing about the frame, since a docked window no longer has a
    /// place of its own. <c>null</c> on a profile without tabs, or saved
    /// before tabs existed.
    /// </summary>
    public WindowPlacement? TabsWindow { get; set; }

    /// <summary>
    /// The accounts that were in the tabbed frame. Empty on a profile
    /// saved before tab mode existed: its accounts then open in free
    /// windows, as they used to.
    /// </summary>
    public List<string> TabbedKeys { get; set; } = [];
}

/// <summary>An instance remembered between two launches.</summary>
public sealed class StoredInstance
{
    public string DeviceId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Android profile name at discovery time, for offline display.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Name chosen by the user.</summary>
    public string? CustomName { get; set; }

    public string? LaunchComponent { get; set; }

    /// <summary>
    /// True if the instance is part of the automatic launch.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// True if the window follows automatic placements: keyboard
    /// traversal, repositioning, side by side, size changes.
    ///
    /// Unchecked, the window is left where it is and everything else
    /// arranges itself without it. It still opens and closes like the
    /// others.
    /// </summary>
    public bool IsManaged { get; set; } = true;

    /// <summary>
    /// True if this account opens in the tabbed frame rather than in a
    /// free window.
    ///
    /// The window stays the same: it is docked into the frame, not
    /// recreated. A docked account escapes automatic placements, which
    /// would otherwise fight the frame.
    /// </summary>
    public bool IsTabbed { get; set; }

    /// <summary>
    /// Rank of the instance in the single list, dense from 0 to n-1.
    ///
    /// This is the only ordering data: instances sort freely among
    /// themselves, regardless of their device. Sorting on this alone is
    /// therefore enough to get the display order, the opening order, and
    /// the keyboard traversal order.
    /// <see cref="InstanceOrdering.Normalize"/> is what keeps it dense.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Where the window was left. <c>null</c> as long as it has never
    /// been opened: placement then falls back to the anchor and size.
    /// </summary>
    public StoredWindowRect? Window { get; set; }

    /// <summary>
    /// Quality tier specific to this account, or <c>null</c> to follow
    /// the shared setting.
    ///
    /// A main account deserves sixty frames and a high bitrate; four
    /// mules following along do not need that, and what is spared them
    /// is that much less processor, bandwidth, heat, and battery.
    /// <c>null</c> by default: nobody has to configure five accounts for
    /// the application to work.
    /// </summary>
    public StreamQuality? Quality { get; set; }

    /// <summary>
    /// In-game distance specific to this account, or <c>null</c> to
    /// follow the shared setting.
    ///
    /// It is set per account for the same reason as the tier, but its
    /// motive differs: this is not a saving, it is a use. One wants a
    /// wide view on the account being played, and it hardly matters what
    /// the mules show when only their health bar is watched. <c>null</c>
    /// by default, like the tier.
    /// </summary>
    public GameZoom? GameZoom { get; set; }

    /// <summary>
    /// Playtime per day, in seconds, over the rolling week. The key is
    /// an ISO date. See <see cref="PlaytimeLog" />.
    /// </summary>
    public Dictionary<string, int> Playtime { get; set; } = [];

    /// <summary>
    /// Stable key of the instance. Excluded from the file: it is derived
    /// from the three fields that make it up, and writing it would only
    /// add a redundancy that a hand edit could contradict.
    /// </summary>
    [JsonIgnore]
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";
}

/// <summary>
/// Retained geometry of a game window. The screen is stored along with
/// it: a rectangle valid yesterday can end up off every screen today,
/// and a window must not be reopened there invisibly.
/// </summary>
public sealed class StoredWindowRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// Screen the window was on. This name is positional: unplugging a
    /// screen renumbers the following ones. It is therefore not enough
    /// on its own, and the bounds are stored alongside it.
    /// </summary>
    public string? MonitorDeviceName { get; set; }

    public int MonitorX { get; set; }
    public int MonitorY { get; set; }
    public int MonitorWidth { get; set; }
    public int MonitorHeight { get; set; }

    /// <summary>Outer rectangle of the window.</summary>
    [JsonIgnore]
    public Windows.ScreenRect Bounds => new(X, Y, Width, Height);

    /// <summary>Screen bounds at the moment of capture.</summary>
    [JsonIgnore]
    public Windows.ScreenRect Monitor => new(MonitorX, MonitorY, MonitorWidth, MonitorHeight);

    public static StoredWindowRect From(Windows.ScreenRect rect, Windows.MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return new StoredWindowRect
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            MonitorDeviceName = monitor.DeviceName,
            MonitorX = monitor.Bounds.X,
            MonitorY = monitor.Bounds.Y,
            MonitorWidth = monitor.Bounds.Width,
            MonitorHeight = monitor.Bounds.Height,
        };
    }
}

/// <summary>Persisted shape of a hotkey.</summary>
public sealed class StoredHotkey
{
    public string Action { get; set; } = string.Empty;
    public int VirtualKey { get; set; }
    public HotkeyModifiers Modifiers { get; set; }

    public static StoredHotkey From(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return new StoredHotkey
        {
            Action = binding.Action.ToString(),
            VirtualKey = binding.VirtualKey,
            Modifiers = binding.Modifiers,
        };
    }

    /// <summary>
    /// Rebuilds a hotkey, or returns <c>null</c> if the action no longer
    /// exists. A file written by another version must not make reading
    /// fail.
    /// </summary>
    public HotkeyBinding? ToBinding()
    {
        // "CloseAll" is the old name for "Quit". Without this mapping,
        // the combination the user chose would be lost at the rename.
        var name = string.Equals(Action, "CloseAll", StringComparison.OrdinalIgnoreCase)
            ? nameof(HotkeyAction.Quit)
            : Action;

        return Enum.TryParse<HotkeyAction>(name, ignoreCase: true, out var action)
            ? new HotkeyBinding { Action = action, VirtualKey = VirtualKey, Modifiers = Modifiers }
            : null;
    }
}

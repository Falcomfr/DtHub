namespace DtHub.Core.Hotkeys;

/// <summary>
/// The actions a hotkey can be associated with. Deliberately few:
/// anything set once lives in the configurator, not in a hotkey.
///
/// The enum is persisted by its name: never rename an existing
/// member without planning a migration.
/// </summary>
public enum HotkeyAction
{
    /// <summary>Show or hide the configurator.</summary>
    ToggleConfigurator,

    /// <summary>Move to the next instance.</summary>
    NextInstance,

    /// <summary>Go back to the previous instance.</summary>
    PreviousInstance,

    /// <summary>Put all windows back in place.</summary>
    Rearrange,

    /// <summary>Two windows, each on half the screen.</summary>
    Tile,

    /// <summary>Show or hide quest tracking.</summary>
    Quests,

    /// <summary>Open today's Almanax.</summary>
    Almanax,

    /// <summary>First size, the smallest.</summary>
    Size1,

    Size2,

    Size3,

    Size4,

    /// <summary>Borderless fullscreen.</summary>
    Fullscreen,

    /// <summary>
    /// Quit the application, including the game windows. Closing the
    /// windows without quitting had no use of its own: leaving them
    /// closed amounted to quitting, without remembering the state.
    /// </summary>
    Quit,
}

/// <summary>Modifier keys, combinable.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,

    /// <summary>
    /// Windows key. Not recommended: the system reserves many of
    /// them.
    /// </summary>
    Windows = 8,
}

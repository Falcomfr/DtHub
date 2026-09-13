using DtHub.Core.Localization;

namespace DtHub.Core.Hotkeys;

/// <summary>Reason why a hotkey is rejected.</summary>
public enum HotkeyValidationResult
{
    Valid,

    /// <summary>No main key was entered.</summary>
    NoKey,

    /// <summary>The main key is a modifier key.</summary>
    ModifierOnly,

    /// <summary>
    /// Without a modifier, the hotkey would trigger on every
    /// keystroke in the mirrored application.
    /// </summary>
    MissingModifier,

    /// <summary>
    /// Combination reserved by Windows, impossible to intercept.
    /// </summary>
    ReservedBySystem,

    /// <summary>
    /// Universal editing key. It would indeed be intercepted, and
    /// that is exactly the problem: <c>RegisterHotKey</c> would
    /// hijack it everywhere, including in the game.
    /// </summary>
    ReservedForEditing,

    /// <summary>Already assigned to another action.</summary>
    Duplicate,
}

/// <summary>A keyboard shortcut associated with an action.</summary>
public sealed record HotkeyBinding
{
    public required HotkeyAction Action { get; init; }

    /// <summary>Windows virtual key code.</summary>
    public required int VirtualKey { get; init; }

    public HotkeyModifiers Modifiers { get; init; } = HotkeyModifiers.None;

    /// <summary>
    /// A hotkey can be deliberately empty: the user has the right to
    /// disable an action.
    /// </summary>
    public bool IsAssigned => VirtualKey != 0;

    /// <summary>Displayed representation, of the form "Ctrl + Tab".</summary>
    public string DisplayText
    {
        get
        {
            if (!IsAssigned)
            {
                return Strings.Get("HotkeyUnassigned");
            }

            List<string> parts = [];

            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                parts.Add(Strings.Get("KeyShift"));
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Windows))
            {
                parts.Add("Win");
            }

            parts.Add(VirtualKeys.Describe(VirtualKey));

            return string.Join(" + ", parts);
        }
    }

    /// <summary>
    /// Comparison key between hotkeys, independent of the action.
    /// </summary>
    public (int Key, HotkeyModifiers Modifiers) Combination => (VirtualKey, Modifiers);

    /// <summary>Label of the action, for the hotkey editor.</summary>
    /// <summary>
    /// What the action does, in one line. The label must name its
    /// reference when it has one: "remettre en place" (put back in
    /// place) did not say on what.
    /// </summary>
    public static string DescribeAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleConfigurator => Strings.Get("ActionToggleSettings"),
        HotkeyAction.NextInstance => Strings.Get("ActionNextWindow"),
        HotkeyAction.PreviousInstance => Strings.Get("ActionPreviousWindow"),
        HotkeyAction.Rearrange => Strings.Get("ActionStackWindows"),
        HotkeyAction.Tile => Strings.Get("SideBySide"),
        HotkeyAction.Quests => Strings.Get("ActionToggleGuides"),
        HotkeyAction.Almanax => Strings.Get("ActionAlmanax"),
        HotkeyAction.Size1 => Strings.Get("ActionSize1"),
        HotkeyAction.Size2 => Strings.Get("ActionSize2"),
        HotkeyAction.Size3 => Strings.Get("ActionSize3"),
        HotkeyAction.Size4 => Strings.Get("ActionSize4"),
        HotkeyAction.Fullscreen => Strings.Get("ActionFullscreen"),
        HotkeyAction.Quit => Strings.Get("Quit"),
        _ => action.ToString(),
    };

    /// <summary>
    /// What the action really does, for the tooltip. The label fits
    /// on one line and cannot say everything; what it leaves unsaid
    /// can be read here.
    /// </summary>
    public static string DetailAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleConfigurator => Strings.Get("ActionToggleSettingsDetail"),
        HotkeyAction.NextInstance => Strings.Get("ActionNextWindowDetail"),
        HotkeyAction.PreviousInstance => Strings.Get("ActionPreviousWindowDetail"),
        HotkeyAction.Rearrange => Strings.Get("ActionStackWindowsDetail"),
        HotkeyAction.Tile => Strings.Get("ActionTileDetail"),
        HotkeyAction.Quests => Strings.Get("ActionToggleGuidesDetail"),
        HotkeyAction.Almanax => Strings.Get("ActionAlmanaxDetail"),
        HotkeyAction.Size1 => Strings.Get("ActionSize1Detail"),
        HotkeyAction.Size2 => Strings.Get("ActionSize2Detail"),
        HotkeyAction.Size3 => Strings.Get("ActionSize3Detail"),
        HotkeyAction.Size4 => Strings.Get("ActionSize4Detail"),
        HotkeyAction.Fullscreen => Strings.Get("ActionFullscreenDetail"),
        HotkeyAction.Quit => Strings.Get("ActionQuitDetail"),

        _ => string.Empty,
    };
}

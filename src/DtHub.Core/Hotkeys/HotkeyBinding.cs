using DtHub.Core.Localization;

namespace DtHub.Core.Hotkeys;

/// <summary>Raison pour laquelle un raccourci est refusé.</summary>
public enum HotkeyValidationResult
{
    Valid,

    /// <summary>Aucune touche principale n'a été saisie.</summary>
    NoKey,

    /// <summary>La touche principale est une touche de modification.</summary>
    ModifierOnly,

    /// <summary>
    /// Sans modificateur, le raccourci se déclencherait à chaque frappe dans
    /// l'application mirrorée.
    /// </summary>
    MissingModifier,

    /// <summary>Combinaison réservée par Windows, impossible à intercepter.</summary>
    ReservedBySystem,

    /// <summary>Déjà attribuée à une autre action.</summary>
    Duplicate,
}

/// <summary>Un raccourci clavier associé à une action.</summary>
public sealed record HotkeyBinding
{
    public required HotkeyAction Action { get; init; }

    /// <summary>Code de touche virtuelle Windows.</summary>
    public required int VirtualKey { get; init; }

    public HotkeyModifiers Modifiers { get; init; } = HotkeyModifiers.None;

    /// <summary>
    /// Un raccourci peut être volontairement vide : l'utilisateur a le droit
    /// de désactiver une action.
    /// </summary>
    public bool IsAssigned => VirtualKey != 0;

    /// <summary>Représentation affichée, du type « Ctrl + Tab ».</summary>
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

    /// <summary>Clé de comparaison entre raccourcis, indépendante de l'action.</summary>
    public (int Key, HotkeyModifiers Modifiers) Combination => (VirtualKey, Modifiers);

    /// <summary>Libellé de l'action, pour l'éditeur de raccourcis.</summary>
    /// <summary>
    /// Ce que fait l'action, en une ligne. Le libellé doit nommer sa référence
    /// quand elle en a une : « remettre en place » ne disait pas sur quoi.
    /// </summary>
    public static string DescribeAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleConfigurator => Strings.Get("ActionToggleSettings"),
        HotkeyAction.NextInstance => Strings.Get("ActionNextWindow"),
        HotkeyAction.PreviousInstance => Strings.Get("ActionPreviousWindow"),
        HotkeyAction.Rearrange => Strings.Get("ActionStackWindows"),
        HotkeyAction.Tile => Strings.Get("SideBySide"),
        HotkeyAction.Quests => Strings.Get("ActionToggleGuides"),
        HotkeyAction.Size1 => Strings.Get("ActionSize1"),
        HotkeyAction.Size2 => Strings.Get("ActionSize2"),
        HotkeyAction.Size3 => Strings.Get("ActionSize3"),
        HotkeyAction.Size4 => Strings.Get("ActionSize4"),
        HotkeyAction.Fullscreen => Strings.Get("ActionFullscreen"),
        HotkeyAction.Quit => Strings.Get("Quit"),
        _ => action.ToString(),
    };

    /// <summary>
    /// Ce que l'action fait vraiment, pour l'infobulle. Le libellé tient sur
    /// une ligne et ne peut pas tout dire ; ce qu'il tait se lit ici.
    /// </summary>
    public static string DetailAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleConfigurator => Strings.Get("ActionToggleSettingsDetail"),
        HotkeyAction.NextInstance => Strings.Get("ActionNextWindowDetail"),
        HotkeyAction.PreviousInstance => Strings.Get("ActionPreviousWindowDetail"),
        HotkeyAction.Rearrange => Strings.Get("ActionStackWindowsDetail"),
        HotkeyAction.Tile => Strings.Get("ActionTileDetail"),
        HotkeyAction.Quests => Strings.Get("ActionToggleGuidesDetail"),
        HotkeyAction.Size1 => Strings.Get("ActionSize1Detail"),
        HotkeyAction.Size2 => Strings.Get("ActionSize2Detail"),
        HotkeyAction.Size3 => Strings.Get("ActionSize3Detail"),
        HotkeyAction.Size4 => Strings.Get("ActionSize4Detail"),
        HotkeyAction.Fullscreen => Strings.Get("ActionFullscreenDetail"),
        HotkeyAction.Quit => Strings.Get("ActionQuitDetail"),

        _ => string.Empty,
    };
}

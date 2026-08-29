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
                return "Non attribué";
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
                parts.Add("Maj");
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
    public static string DescribeAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleConfigurator => "Afficher ou masquer le configurateur",
        HotkeyAction.NextInstance => "Instance suivante",
        HotkeyAction.PreviousInstance => "Instance précédente",
        HotkeyAction.Rearrange => "Remettre les fenêtres en place",
        HotkeyAction.Size1 => "Taille 1",
        HotkeyAction.Size2 => "Taille 2",
        HotkeyAction.Size3 => "Taille 3",
        HotkeyAction.Size4 => "Taille 4",
        HotkeyAction.Fullscreen => "Plein écran",
        HotkeyAction.Quit => "Quitter",
        _ => action.ToString(),
    };
}

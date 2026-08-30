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
    /// <summary>
    /// Ce que fait l'action, en une ligne. Le libellé doit nommer sa référence
    /// quand elle en a une : « remettre en place » ne disait pas sur quoi.
    /// </summary>
    public static string DescribeAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleConfigurator => "Afficher ou masquer les réglages",
        HotkeyAction.NextInstance => "Fenêtre suivante",
        HotkeyAction.PreviousInstance => "Fenêtre précédente",
        HotkeyAction.Rearrange => "Empiler les fenêtres",
        HotkeyAction.Tile => "Côte à côte",
        HotkeyAction.Size1 => "Taille 1",
        HotkeyAction.Size2 => "Taille 2",
        HotkeyAction.Size3 => "Taille 3",
        HotkeyAction.Size4 => "Taille 4",
        HotkeyAction.Fullscreen => "Plein écran",
        HotkeyAction.Quit => "Quitter",
        _ => action.ToString(),
    };

    /// <summary>
    /// Ce que l'action fait vraiment, pour l'infobulle. Le libellé tient sur
    /// une ligne et ne peut pas tout dire ; ce qu'il tait se lit ici.
    /// </summary>
    public static string DetailAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleConfigurator =>
            "Montre ou cache cette fenêtre. Les fenêtres de jeu restent ouvertes, "
            + "et le rappel de cette combinaison figure dans leur titre.",

        HotkeyAction.NextInstance =>
            "Passe à la fenêtre suivante, dans l'ordre de la liste des appareils. "
            + "Les fenêtres mises de côté sont sautées.",

        HotkeyAction.PreviousInstance =>
            "Passe à la fenêtre précédente, dans l'ordre de la liste des appareils. "
            + "Les fenêtres mises de côté sont sautées.",

        HotkeyAction.Rearrange =>
            "Empile toutes les fenêtres sur la dernière que vous avez utilisée, "
            + "à sa position et à sa taille. Les fenêtres verrouillées ne bougent pas.",

        HotkeyAction.Tile =>
            "Range deux fenêtres côte à côte, chacune sur une moitié de l'écran. "
            + "La fenêtre active va à droite.",

        HotkeyAction.Size1 => "La plus petite des quatre tailles, réglables au curseur.",
        HotkeyAction.Size2 => "La deuxième des quatre tailles, réglables au curseur.",
        HotkeyAction.Size3 => "La troisième des quatre tailles, réglables au curseur.",

        HotkeyAction.Size4 =>
            "La plus grande des quatre tailles. Elle couvre la zone utile de l'écran, "
            + "barre des tâches exclue : ce n'est pas le plein écran.",

        HotkeyAction.Fullscreen =>
            "Couvre l'écran entier, sans bordure. Y revenir rend à chaque fenêtre "
            + "la place qu'elle avait.",

        HotkeyAction.Quit => "Ferme l'application et toutes les fenêtres de jeu.",

        _ => string.Empty,
    };
}

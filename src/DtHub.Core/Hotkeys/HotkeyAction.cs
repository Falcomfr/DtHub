namespace DtHub.Core.Hotkeys;

/// <summary>
/// Les actions auxquelles un raccourci peut être associé. Volontairement peu
/// nombreuses : tout ce qui se règle une fois vit dans le configurateur, pas
/// dans un raccourci.
///
/// L'énumération est persistée par son nom : ne jamais renommer un membre
/// existant sans prévoir une migration.
/// </summary>
public enum HotkeyAction
{
    /// <summary>Afficher ou masquer le configurateur.</summary>
    ToggleConfigurator,

    /// <summary>Passer à l'instance suivante.</summary>
    NextInstance,

    /// <summary>Revenir à l'instance précédente.</summary>
    PreviousInstance,

    /// <summary>Remettre toutes les fenêtres en place.</summary>
    Rearrange,

    /// <summary>Deux fenêtres, chacune sur une moitié de l'écran.</summary>
    Tile,

    /// <summary>Afficher ou masquer le suivi de quêtes.</summary>
    Quests,

    /// <summary>Ouvrir l'Almanax du jour.</summary>
    Almanax,

    /// <summary>Première taille, la plus petite.</summary>
    Size1,

    Size2,

    Size3,

    Size4,

    /// <summary>Plein écran sans bordure.</summary>
    Fullscreen,

    /// <summary>
    /// Quitter l'application, fenêtres de jeu comprises. Fermer les fenêtres
    /// sans quitter n'avait pas d'usage propre : les laisser fermées revenait
    /// à quitter, sans en retenir l'état.
    /// </summary>
    Quit,
}

/// <summary>Touches de modification, combinables.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,

    /// <summary>Touche Windows. Déconseillée : le système en réserve beaucoup.</summary>
    Windows = 8,
}

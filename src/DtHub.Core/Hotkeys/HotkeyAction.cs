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

    /// <summary>Fermer toutes les fenêtres de jeu ouvertes par l'application.</summary>
    CloseAll,
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

namespace DtHub.Core.Hotkeys;

/// <summary>
/// Actions auxquelles un raccourci peut être associé. L'énumération est
/// persistée par son nom : ne jamais renommer un membre existant sans prévoir
/// une migration.
/// </summary>
public enum HotkeyAction
{
    /// <summary>Passer à la session suivante du profil.</summary>
    NextSession,

    /// <summary>Session précédente. Complément naturel de la précédente.</summary>
    PreviousSession,

    Size1,
    Size2,
    Size3,
    Size4,

    /// <summary>Plein écran sans bordure.</summary>
    Fullscreen,

    /// <summary>Remettre toutes les fenêtres ensemble.</summary>
    Recenter,

    /// <summary>Fermer toutes les sessions ouvertes par DT Hub.</summary>
    CloseAllSessions,

    /// <summary>Ouvrir la page des paramètres.</summary>
    OpenSettings,
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

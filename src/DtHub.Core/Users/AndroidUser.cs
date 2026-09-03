using DtHub.Core.Localization;

namespace DtHub.Core.Users;

/// <summary>
/// Un utilisateur ou profil Android d'un téléphone. Chaque utilisateur a son
/// propre jeu d'applications installées, ce qui permet de lancer deux fois la
/// même application dans deux sessions distinctes.
/// </summary>
public sealed record AndroidUser
{
    /// <summary>
    /// Identifiant Android. N'importe quel entier positif est valide : aucune
    /// valeur particulière ne doit être supposée.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>Nom tel que le téléphone le rapporte, souvent traduit.</summary>
    public required string Name { get; init; }

    /// <summary>Drapeaux bruts, conservés pour le diagnostic.</summary>
    public int Flags { get; init; }

    public AndroidUserType Type { get; init; } = AndroidUserType.Unknown;

    /// <summary>
    /// Un utilisateur arrêté ne peut pas lancer d'application tant qu'il n'a
    /// pas été démarré.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Un profil en pause existe, se liste, et ne lance rien. C'est
    /// l'interrupteur du profil professionnel, et la fonction principale de
    /// Shelter et d'Island. Le téléphone seul peut le rallumer : aucune
    /// commande ADB ne le permet, vérifié sur Android 16 où
    /// <c>cmd user set-quiet-mode</c> n'existe pas.
    /// </summary>
    public bool IsPaused { get; init; }

    public bool IsPrimary => Type == AndroidUserType.Primary;

    /// <summary>Libellé court du type, pour les infobulles et le diagnostic.</summary>
    public string TypeLabel => Type switch
    {
        AndroidUserType.Primary => "Principal",
        AndroidUserType.ManagedProfile => Strings.Get("ManagedProfile"),
        AndroidUserType.CloneProfile => "Clone",
        AndroidUserType.Secondary => "Second espace",
        AndroidUserType.Guest => Strings.Get("Guest"),
        AndroidUserType.Restricted => "Restreint",
        _ => "Profil",
    };

    /// <summary>
    /// Nom affiché dans les listes. Le téléphone nomme lui-même ses profils,
    /// souvent mieux que nous ne saurions le faire : « Applications
    /// dupliquées », « Second espace ». On lui laisse la main, sauf pour
    /// l'utilisateur principal où un libellé stable est plus clair.
    /// </summary>
    public string DisplayName => Type == AndroidUserType.Primary
        ? "Principal"
        : string.IsNullOrWhiteSpace(Name) ? $"{TypeLabel} {Id}" : Name.Trim();
}

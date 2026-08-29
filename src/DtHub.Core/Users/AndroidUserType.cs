namespace DtHub.Core.Users;

/// <summary>
/// Nature d'un utilisateur Android. Elle est déduite des drapeaux rapportés
/// par <c>pm list users</c>, affinée par <c>dumpsys user</c> quand celui-ci
/// répond. Rien n'est déduit de l'identifiant : un clone ne vaut pas toujours
/// 999, ni un profil professionnel toujours 10.
/// </summary>
public enum AndroidUserType
{
    /// <summary>Drapeaux inconnus ou illisibles.</summary>
    Unknown = 0,

    /// <summary>Utilisateur principal du téléphone.</summary>
    Primary,

    /// <summary>
    /// Profil géré. Android range sous ce type aussi bien un profil
    /// professionnel qu'une duplication d'applications selon les surcouches
    /// constructeur, d'où un libellé neutre.
    /// </summary>
    ManagedProfile,

    /// <summary>Profil de clonage d'applications.</summary>
    CloneProfile,

    /// <summary>Second espace ou utilisateur secondaire complet.</summary>
    Secondary,

    Guest,

    Restricted,
}

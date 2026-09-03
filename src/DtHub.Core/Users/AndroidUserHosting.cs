using DtHub.Core.Localization;

namespace DtHub.Core.Users;

/// <summary>
/// Ce qu'un profil Android peut faire apparaître à l'écran pendant que les
/// autres sont ouverts.
///
/// La règle n'est pas une opinion : elle vient d'une mesure faite sur un
/// Xiaomi 23078PND5G sous Android 16, afficheur virtuel créé par scrcpy, en
/// interrogeant l'oracle qu'Android publie lui-même,
/// <c>cmd user is-user-visible --display D N</c>.
///
/// <list type="table">
///   <item>
///     <term>Profil géré, identifiant 15, drapeaux 0x1030</term>
///     <description>visible : vrai. Le jeu s'ouvre en quatre secondes,
///     <c>LaunchState: COLD</c>, écran de connexion complet.</description>
///   </item>
///   <item>
///     <term>Utilisateur complet, identifiant 14, drapeaux 0x400</term>
///     <description>visible : faux. <c>am start</c> répond pourtant
///     <c>Status: ok</c>, puis pend soixante-dix secondes sans rien
///     afficher.</description>
///   </item>
/// </list>
///
/// C'est la raison d'être de ce fichier : <c>am start</c> annonce un succès là
/// où rien ne s'affichera jamais. Se fier à lui revient à promettre une fenêtre
/// qui ne viendra pas.
///
/// Un profil suit toujours son parent : dès que l'utilisateur principal est
/// visible, ses profils le sont aussi, sur n'importe quel afficheur. Un
/// utilisateur complet, lui, ne peut être visible en arrière-plan que si
/// l'appareil l'autorise, ce que dit
/// <c>cmd user is-visible-background-users-supported</c> : faux sur un
/// téléphone ordinaire, vrai sur les systèmes embarqués automobiles.
/// </summary>
public static class AndroidUserHosting
{
    /// <summary>
    /// Dit si ce profil peut porter une fenêtre de jeu pendant que les autres
    /// sont ouverts, et sinon pourquoi.
    /// </summary>
    /// <param name="user">Le profil examiné.</param>
    /// <param name="visibleBackgroundUsers">
    /// Ce que l'appareil répond à <c>is-visible-background-users-supported</c>.
    /// <c>null</c> quand la question n'a pas été posée ou n'a pas abouti :
    /// on s'en tient alors au comportement des téléphones ordinaires.
    /// </param>
    public static AndroidUserHosting.Verdict Describe(
        AndroidUser user,
        bool? visibleBackgroundUsers = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        // La pause passe avant le type : un profil professionnel en pause ne
        // lance rien, quelle que soit sa nature par ailleurs.
        if (user.IsPaused)
        {
            return new Verdict(
                false,
                Strings.Get("ProfilePausedShort"));
        }

        switch (user.Type)
        {
            case AndroidUserType.Primary:
            case AndroidUserType.ManagedProfile:
            case AndroidUserType.CloneProfile:
                return new Verdict(true, string.Empty);

            case AndroidUserType.Secondary:
                return visibleBackgroundUsers == true
                    ? new Verdict(true, string.Empty)
                    : new Verdict(
                        false,
                        Strings.Get("OneFullUserAtATime"));

            case AndroidUserType.Guest:
                return new Verdict(
                    false,
                    Strings.Get("GuestProfileWiped"));

            case AndroidUserType.Restricted:
                return new Verdict(
                    false,
                    Strings.Get("RestrictedProfile"));

            default:
                return new Verdict(
                    false,
                    Strings.Get("UnknownProfileKind"));
        }
    }

    /// <summary>
    /// Réponse de la règle. <see cref="Reason"/> est vide quand le profil
    /// convient, et porte sinon une phrase montrable telle quelle.
    /// </summary>
    public readonly record struct Verdict(bool CanHostWindow, string Reason);
}

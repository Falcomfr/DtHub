namespace DtHub.Core.Windows;

/// <summary>
/// Décide des raccourcis à écrire au démarrage.
///
/// L'application n'est pas installée par un programme d'installation : c'est un
/// fichier qu'on pose où l'on veut. Sans raccourci, il faut aller le rechercher
/// là où on l'a mis.
/// </summary>
public static class ShortcutPlacement
{
    /// <summary>
    /// Vrai si le raccourci du bureau doit être écrit.
    ///
    /// Deux besoins se contredisent. Il faut poser le raccourci sans rien
    /// demander, sinon personne ne l'a. Et il ne faut pas le reposer quand on
    /// vient de l'effacer, sinon l'application impose sa présence sur le bureau
    /// de quelqu'un à chaque démarrage.
    ///
    /// Le raccourci du menu Démarrer, lui, est toujours récrit : il tranche
    /// entre les deux. Son absence signe une première installation, où l'on
    /// pose les deux. Sa présence signe une installation déjà connue, où un
    /// bureau vide est un choix qu'on respecte.
    ///
    /// Reste le cas d'un exécutable déplacé : le raccourci du bureau viserait
    /// l'ancien emplacement pour toujours. C'est pourquoi un raccourci qui
    /// existe est récrit, exactement comme celui du menu Démarrer.
    /// </summary>
    /// <param name="desktopLinkExists">Un raccourci est déjà sur le bureau.</param>
    /// <param name="startMenuLinkExists">Un raccourci est déjà au menu Démarrer.</param>
    public static bool ShouldWriteDesktop(bool desktopLinkExists, bool startMenuLinkExists) =>
        desktopLinkExists || !startMenuLinkExists;
}

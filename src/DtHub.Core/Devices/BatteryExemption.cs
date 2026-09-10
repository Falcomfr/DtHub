namespace DtHub.Core.Devices;

/// <summary>
/// Le jeu est-il à l'abri de l'économie d'énergie sur cet appareil.
///
/// **Le défaut que ce type existe pour attraper : une aide qu'on ne suit
/// pas.** L'application explique depuis longtemps qu'il faut retirer le jeu
/// des restrictions de batterie, et c'est la première cause des fenêtres qui
/// se figent une à une. Mais elle ne vérifiait jamais que c'était fait. Deux
/// téléphones du même utilisateur, relevés le même jour :
///
/// <code>
/// 13T Pro   user,com.ankama.dofustouch,10475   <- préparé
/// Mi 9T Pro (rien)                             <- jamais fait
/// </code>
///
/// Le second est justement celui qui se déconnecte. L'aide était bonne, elle
/// n'avait simplement pas été appliquée là, et rien ne le disait.
///
/// **Le contrôle ne connaît aucune marque, et c'est voulu.** Il lit la liste
/// d'Android, pas celle d'un constructeur : la même commande répond sur les
/// sept familles décrites par l'aide. Mais l'inverse n'est pas garanti, et il
/// faut le dire : le chemin de menu que chaque marque propose n'écrit pas
/// forcément dans cette liste. Mesuré sur Xiaomi, « Économiseur de batterie ›
/// Aucune restriction » y écrit bien. Chez Samsung, Honor et vivo, le menu
/// que l'aide donne est une liste maison, qui peut laisser celle d'Android
/// vide. Le message nomme donc le réglage qui compte pour Android, « Sans
/// restriction » dans la fiche batterie du jeu, et renvoie à l'aide pour ce
/// que la marque demande en plus.
///
/// **Le contrôle porte sur l'appareil, pas sur le compte.** Android tient sa
/// liste par paquet et par identifiant d'application, et une copie du jeu
/// dans un second profil est une application distincte, avec sa propre
/// restriction. Conclure compte par compte demanderait de deviner
/// l'identifiant de chaque copie. Dire « ce téléphone n'a jamais été
/// préparé » est la chose vraie qu'on peut affirmer, et c'est déjà celle qui
/// manque.
/// </summary>
public static class BatteryExemption
{
    /// <summary>
    /// Vrai si le paquet figure dans la liste, <c>null</c> quand la réponse
    /// n'apprend rien.
    ///
    /// <c>null</c> et non faux sur une réponse vide : tous les appareils vus
    /// y portent des dizaines d'entrées système, si bien qu'une liste vide
    /// dit que la commande a échoué, pas que rien n'est exempté.
    /// </summary>
    /// <param name="whitelist">Sortie de <c>dumpsys deviceidle whitelist</c>.</param>
    /// <param name="package">Nom du paquet cherché.</param>
    public static bool? Covers(string? whitelist, string? package)
    {
        if (string.IsNullOrWhiteSpace(whitelist) || string.IsNullOrWhiteSpace(package))
        {
            return null;
        }

        var found = false;
        var entries = 0;

        foreach (var line in whitelist.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            // Chaque ligne s'écrit « origine,paquet,identifiant ». L'origine
            // dit qui a posé l'exemption, système ou utilisateur ; les deux
            // protègent de la même façon, et seule la présence compte.
            var fields = line.Split(',');

            if (fields.Length < 2)
            {
                continue;
            }

            entries++;

            found |= string.Equals(fields[1].Trim(), package.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return entries == 0 ? null : found;
    }
}

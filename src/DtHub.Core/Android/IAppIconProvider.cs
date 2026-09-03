namespace DtHub.Core.Android;

/// <summary>De quoi aller chercher une icône sur un téléphone précis.</summary>
/// <param name="DeviceId">Identité stable de l'appareil, qui sert de clef.</param>
/// <param name="Serial">Numéro de série ADB, qui est une adresse en sans-fil.</param>
/// <param name="UserId">
/// Profil où chercher le paquet. Une application installée sur le seul profil
/// cloné est invisible à <c>pm path</c> sans lui. Le résultat, lui, ne dépend
/// pas du profil : c'est la même archive pour tous.
/// </param>
/// <param name="PackageName">Paquet dont on veut l'icône.</param>
public readonly record struct AppIconRequest(
    string DeviceId,
    string Serial,
    int UserId,
    string PackageName);

/// <summary>
/// Rend l'icône d'une application telle qu'elle est sur le téléphone, extraite
/// d'une seule entrée de son archive et posée dans le cache.
///
/// Ne lève jamais. Une icône est une décoration : ni un téléphone sans
/// <c>unzip</c>, ni une archive sans image matricielle, ni un refus d'ADB ne
/// justifient de faire échouer quoi que ce soit. Dans tous ces cas la méthode
/// rend <c>null</c>, et la liste s'affiche exactement comme sans elle.
/// </summary>
public interface IAppIconProvider
{
    /// <summary>
    /// Ce qu'on sait déjà, sans rien demander au téléphone. C'est ce que le
    /// balayage appelle : il passe toutes les trois secondes et ne doit pas
    /// s'allonger d'une seule commande.
    /// </summary>
    string? Find(string deviceId, string packageName);

    /// <summary>
    /// Le chemin de l'icône, extraite si besoin, ou <c>null</c>. Ne lève
    /// jamais, ce qui est ce qui autorise l'appelant à ne pas l'attendre.
    /// </summary>
    Task<string?> GetAsync(AppIconRequest request, CancellationToken cancellationToken = default);
}

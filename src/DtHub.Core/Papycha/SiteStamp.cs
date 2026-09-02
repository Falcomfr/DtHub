namespace DtHub.Core.Papycha;

/// <summary>
/// De quoi savoir que le site a bougé sans le relire.
///
/// Deux nombres suffisent : la date du dernier article modifié et le nombre
/// total d'articles. L'un bouge quand une page est corrigée, l'autre quand une
/// page paraît ou disparaît.
///
/// La demande qui les rend pèse quatre-vingt-dix-sept octets, contre dix-huit
/// millions pour relire le site en entier. C'est ce qui permet de regarder
/// souvent au lieu d'attendre une semaine.
/// </summary>
/// <param name="Modified">Date du dernier article modifié.</param>
/// <param name="Posts">Nombre total d'articles publiés.</param>
public sealed record SiteStamp(DateTimeOffset Modified, int Posts);

/// <summary>
/// L'empreinte d'une catégorie du site, retenue d'une lecture à l'autre.
///
/// Une empreinte pour tout le site déclenchait une relecture dès qu'un seul des
/// mille douze articles bougeait, même un dont on ne lit rien. Elles sont donc
/// prises catégorie par catégorie, cinq demandes de quatre-vingt-dix-sept
/// octets, et seule celle qui a bougé se relit.
/// </summary>
/// <param name="Category">La catégorie du site.</param>
/// <param name="Modified">Date du dernier article modifié dans cette catégorie.</param>
/// <param name="Posts">Nombre d'articles qu'elle range.</param>
public sealed record CategoryStamp(int Category, DateTimeOffset Modified, int Posts);

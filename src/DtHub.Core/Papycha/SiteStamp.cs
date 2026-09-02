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

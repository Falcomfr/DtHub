namespace DtHub.Core.Almanax;

/// <summary>
/// Une journée de l'Almanax, réduite à ce qu'on affiche.
///
/// La page du portail porte bien davantage : le protecteur du mois, le signe
/// du zodiaque, la Rubrikabrax, et leurs textes d'ambiance. Rien de tout cela
/// n'aide à savoir quoi apporter aujourd'hui, et c'est la seule question que
/// la fenêtre sert.
/// </summary>
/// <param name="Date">Le jour du calendrier, tel qu'on l'a demandé.</param>
/// <param name="DofusianDay">Le jour dans le calendrier du Monde des Douze, « 10 Septange ».</param>
/// <param name="Offering">La phrase entière, celle qui reste lisible quand la lecture fine échoue.</param>
/// <param name="Quantity">Combien en apporter, si la phrase se laisse lire.</param>
/// <param name="Item">Quoi apporter, si la phrase se laisse lire.</param>
/// <param name="Bonus">Le bonus du jour, sans son préfixe.</param>
/// <param name="BonusDetail">Ce que le bonus fait.</param>
/// <param name="Quest">Le nom de la quête d'offrande, sans son préfixe.</param>
/// <param name="Meryde">Le Méryde du jour.</param>
public sealed record AlmanaxDay(
    DateOnly Date,
    string DofusianDay,
    string Offering,
    int? Quantity,
    string? Item,
    string Bonus,
    string BonusDetail,
    string Quest,
    string Meryde);

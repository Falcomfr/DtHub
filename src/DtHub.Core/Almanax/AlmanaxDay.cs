namespace DtHub.Core.Almanax;

/// <summary>
/// One day of the Almanax, reduced to what is displayed.
///
/// The portal page carries much more: the month's protector, the
/// zodiac sign, the Rubrikabrax, and their flavor texts. None of
/// that helps to know what to bring today, and that is the only
/// question the window serves.
/// </summary>
/// <param name="Date">The calendar day, as it was requested.</param>
/// <param name="DofusianDay">
/// The day in the World of Twelve's calendar, "10 Septange".
/// </param>
/// <param name="Offering">
/// The full sentence, the one that stays readable when
/// fine-grained parsing fails.
/// </param>
/// <param name="Quantity">
/// How many to bring, if the sentence can be parsed.
/// </param>
/// <param name="Item">What to bring, if the sentence can be parsed.</param>
/// <param name="Bonus">The day's bonus, without its prefix.</param>
/// <param name="BonusDetail">What the bonus does.</param>
/// <param name="Quest">
/// The name of the offering quest, without its prefix.
/// </param>
/// <param name="Meryde">The day's Meryde.</param>
/// <param name="MonthEvent">
/// The month's event, date and name together as one, empty if
/// there is none. It does not change from one day to the next:
/// this is what is being prepared by looking at the coming days.
/// </param>
public sealed record AlmanaxDay(
    DateOnly Date,
    string DofusianDay,
    string Offering,
    int? Quantity,
    string? Item,
    string Bonus,
    string BonusDetail,
    string Quest,
    string Meryde,
    string MonthEvent);

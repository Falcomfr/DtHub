namespace DtHub.Core.Almanax;

/// <summary>
/// Searching the Almanax by profession: "mineur" finds the days the
/// miners gain something, and the days that are good for every
/// gatherer without naming them.
///
/// Read over the whole 2027 Touch calendar in the three languages:
/// a profession is named in the bonus detail, never in its title
/// ("Minerai abondant", then "récupérées par les Mineurs"), about
/// twelve days a year for a gathering one and two to four for a
/// crafting one. Some twenty other days are for all of them, or for
/// all gatherers, or for all craftsmen in a zone, and name none.
///
/// Every language is checked whatever the application speaks: the
/// portal page is in the application's language, and a name from
/// another language simply never appears in it.
/// </summary>
public static class AlmanaxProfessions
{
    private sealed record Profession(bool Gathers, string[] Names);

    // Traps found in the reading. "smith" is also in "Blacksmith
    // Dungeon", and "herreros" in "Mazmorra de los Herreros": the weapon
    // smiths are named by what only they carry. The general days are
    // matched on their whole phrase, never on the bare word: "artisans",
    // "craftsmen" and "artesanos" are also the Dark Craftsmen monsters,
    // and "craftsmen" the consumables day, which is not for all of them.
    private static readonly Profession[] All =
    [
        new(true, ["alchimiste", "alchemist", "alquimista"]),
        new(true, ["bûcheron", "lumberjack", "leñador"]),
        new(true, ["chasseur", "hunter", "cazador"]),
        new(true, ["mineur", "miner", "minero"]),
        new(true, ["paysan", "farmer", "campesino"]),
        new(true, ["pêcheur", "fisherm", "pescador"]),
        new(false, ["boulanger", "baker", "panadero"]),
        new(false, ["boucher", "butcher", "carnicero"]),
        new(false, ["poissonnier", "fishmonger", "pescadero"]),
        new(false, ["bijoutier", "jeweller", "joyero"]),
        new(false, ["cordonnier", "shoemaker", "zapatero"]),
        new(false, ["tailleur", "tailor", "sastre"]),
        new(false, ["forgeur", "weaponsmith", "weapon smith", "forjador", "herreros y escultores"]),
        new(false, ["sculpteur", "carver", "escultor"]),
        new(false, ["façonneur", "artificer", "fabricante"]),
        new(false, ["bricoleur", "handym", "manitas"]),
    ];

    /// <summary>What every profession that crafts shares.</summary>
    private static readonly string[] Crafters =
    [
        "tous les métiers", "artisans économisent", "artisans ont", "artisans et les récolteurs",
        "all professions", "craftsmen save", "craftsmen have", "harvesters and craftsmen",
        "todos los oficios", "artesanos ahorran", "artesanos tienen",
    ];

    /// <summary>
    /// What every gatherer shares, on top of the crafters' days: in
    /// Touch, every gathering profession also crafts.
    /// </summary>
    private static readonly string[] Gatherers =
    [
        .. Crafters,
        "métiers de récolte", "récolteurs économisent",
        "harvesting professions", "harvesters save",
        "oficios de recolección", "recolectores ahorran",
    ];

    /// <summary>
    /// True when the day's bonus concerns what was typed: written in
    /// its title or detail, or, when a profession was typed, a bonus
    /// for its kind of profession as a whole.
    /// </summary>
    public static bool Matches(AlmanaxDay day, string? query)
    {
        ArgumentNullException.ThrowIfNull(day);

        var text = day.Bonus + " " + day.BonusDetail;

        if (AlmanaxReading.Mentions(text, query))
        {
            return true;
        }

        if (Named(query) is not { } profession)
        {
            return false;
        }

        return profession.Names.Any(name => AlmanaxReading.Mentions(text, name))
            || (profession.Gathers ? Gatherers : Crafters).Any(phrase => AlmanaxReading.Mentions(text, phrase));
    }

    /// <summary>
    /// The profession typed, in full or in part: "mine" is enough for
    /// the miners, "mineurs" too.
    /// </summary>
    private static Profession? Named(string? query)
    {
        var typed = AlmanaxReading.Clean(query);

        if (typed.Length < 3)
        {
            return null;
        }

        // A name that starts like the word typed comes first: the weapon
        // smiths' Spanish name, "herreros y escultores", contains "escultor",
        // and the carvers' search took the smiths' days.
        return All.FirstOrDefault(p => p.Names.Any(name => StartsAlike(AlmanaxReading.Clean(name), typed)))
            ?? All.FirstOrDefault(p => p.Names.Any(name =>
                AlmanaxReading.Mentions(name, typed) || AlmanaxReading.Mentions(typed, name)));
    }

    /// <summary>Case and accents aside, as <see cref="AlmanaxReading.Mentions" /> compares.</summary>
    private static bool StartsAlike(string name, string typed) =>
        name.Length > 0
        && (Prefix(name, typed) || Prefix(typed, name));

    private static bool Prefix(string text, string start) =>
        System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IsPrefix(
            text, start, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace);
}

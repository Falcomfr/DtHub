namespace DtHub.Core.Almanax;

/// <summary>
/// Jusqu'où l'Almanax se laisse consulter.
///
/// Le calendrier n'a pas de fin en soi : il se répète chaque année sur la date
/// grégorienne, et le portail répond pour n'importe quelle date. Mesuré, il
/// rend la même journée en 2026, 2050 et 2100, et il recalcule correctement
/// les sept mérydes à date mobile : Etnop tombe le 5 avril 2026, le 28 mars
/// 2027 et le 21 avril 2030, c'est-à-dire les vrais dimanches de Pâques.
///
/// **Deux choses imposent pourtant une borne.**
///
/// La première est un mensonge du portail. Interrogé sur une date hors de ce
/// qu'il sait traiter, « 0001-01-01 » ou un mois numéro treize, il ne se
/// plaint pas : il rend **la journée du jour**. Notre bandeau annoncerait donc
/// une date et montrerait l'offrande d'une autre, sans que rien ne le dise.
///
/// La seconde est <c>DateOnly</c> lui-même, qui va de l'an 1 au 31 décembre
/// 9999 : reculer d'un jour depuis sa première date lève, et la fenêtre
/// mourrait sur un clic de flèche.
///
/// Cinq ans de part et d'autre, donc. C'est loin devant tout usage réel, le
/// calendrier ne disant rien de neuf au-delà d'un an sinon les fêtes mobiles,
/// et c'est très à l'écart des deux pièges.
/// </summary>
public static class AlmanaxRange
{
    /// <summary>Années consultables de part et d'autre du jour même.</summary>
    public const int Years = 5;

    /// <summary>La plus ancienne date consultable.</summary>
    public static DateOnly Earliest(DateOnly today) => today.AddYears(-Years);

    /// <summary>La plus lointaine date consultable.</summary>
    public static DateOnly Latest(DateOnly today) => today.AddYears(Years);

    /// <summary>Vrai si la date se consulte.</summary>
    public static bool Contains(DateOnly date, DateOnly today) =>
        date >= Earliest(today) && date <= Latest(today);

    /// <summary>
    /// La date ramenée dans les bornes.
    ///
    /// Ramener plutôt que refuser : une flèche pressée une fois de trop doit
    /// s'arrêter au bord, pas ne rien faire d'inexplicable.
    /// </summary>
    public static DateOnly Clamp(DateOnly date, DateOnly today)
    {
        var first = Earliest(today);
        var last = Latest(today);

        return date < first ? first : date > last ? last : date;
    }

    /// <summary>
    /// Le décalage de mois appliqué sans jamais sortir des bornes.
    ///
    /// Le mois se déplace entier, et le premier du mois obtenu peut tomber
    /// avant la borne alors que le mois lui-même la contient encore : on
    /// compare donc sur le mois, pas sur le jour.
    /// </summary>
    public static DateOnly ShiftMonth(DateOnly month, int months, DateOnly today)
    {
        var first = Earliest(today);
        var last = Latest(today);

        // Le décalage se compte en mois entiers plutôt que de s'ajouter à la
        // date : « AddMonths » lève de lui-même avant qu'on ait pu borner, et
        // c'est une épreuve nommée qui me l'a montré.
        var wanted = Index(month.Year, month.Month) + months;

        wanted = Math.Clamp(wanted, Index(first.Year, first.Month), Index(last.Year, last.Month));

        return new DateOnly((int)(wanted / 12), (int)(wanted % 12) + 1, 1);
    }

    /// <summary>Le rang d'un mois depuis l'an zéro, pour compter sans déborder.</summary>
    private static long Index(int year, int month) => ((long)year * 12) + month - 1;
}

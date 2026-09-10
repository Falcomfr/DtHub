using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// Ce que l'appareil dit de sa batterie.
///
/// C'est la limite dont personne ne parle et qui arrête pourtant les longues
/// séances. Le jeu consomme trente à cinquante pour cent par heure sur un
/// téléphone, et en Wi-Fi l'appareil n'est pas branché : une séance de trois
/// heures se termine par un téléphone éteint, au milieu d'un donjon.
///
/// L'application lisait déjà la chaleur et la liaison, mais pas cela.
///
/// **Rien n'est dit quand l'appareil est en charge.** Un téléphone branché
/// descend rarement, et un bandeau qui annonce quarante pour cent alors que la
/// prise est mise parle pour ne rien dire. Ce qui compte n'est pas le niveau,
/// c'est le niveau qui baisse.
/// </summary>
/// <param name="Percent">Niveau de charge, de 0 à 100.</param>
/// <param name="Charging">Vrai si l'appareil se recharge, ou est plein sur secteur.</param>
/// <param name="Celsius">Température de la batterie, pour le journal, si elle est dite.</param>
public sealed partial record BatteryReading(int Percent, bool Charging, double? Celsius)
{
    /// <summary>En dessous, la séance ne tiendra pas la soirée.</summary>
    public const int Low = 20;

    /// <summary>En dessous, il reste quelques minutes.</summary>
    public const int Critical = 10;

    /// <summary>Vrai quand le niveau mérite qu'on en parle, charge exclue.</summary>
    public bool IsLow => !Charging && Percent <= Low;

    /// <summary>
    /// Lit la sortie de <c>dumpsys battery</c>. Rend <c>null</c> dès que le
    /// niveau manque : ne rien savoir est un cas ordinaire, et l'appelant s'en
    /// passe, comme pour la chaleur.
    /// </summary>
    public static BatteryReading? Parse(string? dumpsys)
    {
        if (string.IsNullOrWhiteSpace(dumpsys))
        {
            return null;
        }

        if (Number(dumpsys, LevelPattern()) is not { } level)
        {
            return null;
        }

        // L'échelle vaut cent partout où on l'a vue, mais elle est déclarée :
        // s'en remettre à cent serait supposer ce que l'appareil dit déjà.
        var scale = Number(dumpsys, ScalePattern()) ?? 100;

        if (scale <= 0)
        {
            return null;
        }

        var percent = (int)Math.Round(level * 100.0 / scale);

        return new BatteryReading(Math.Clamp(percent, 0, 100), IsCharging(dumpsys), Temperature(dumpsys));
    }

    /// <summary>
    /// Ce qu'il y a à dire, ou <c>null</c> quand il n'y a rien à dire.
    /// </summary>
    public string? Describe() => !Charging && Percent <= Critical
        ? Strings.Format("DeviceBatteryCritical", Percent)
        : IsLow
            ? Strings.Format("DeviceBatteryLow", Percent)
            : null;

    /// <summary>
    /// Vrai si l'appareil reçoit du courant.
    ///
    /// Deux sources concordantes, et c'est voulu : l'état numérique vaut 2 en
    /// charge et 5 quand la batterie est pleine, mais un appareil plein qu'on
    /// vient de débrancher garde parfois 5 le temps d'une lecture. Les lignes
    /// « … powered » disent la prise elle-même.
    /// </summary>
    private static bool IsCharging(string dumpsys) =>
        Flag(dumpsys, "AC powered")
        || Flag(dumpsys, "USB powered")
        || Flag(dumpsys, "Wireless powered")
        || Flag(dumpsys, "Dock powered")
        || Number(dumpsys, StatusPattern()) is 2 or 5;

    private static bool Flag(string dumpsys, string label)
    {
        var at = dumpsys.IndexOf(label, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return false;
        }

        var line = dumpsys[at..];
        var end = line.IndexOf('\n', StringComparison.Ordinal);

        return (end < 0 ? line : line[..end]).Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>La température, donnée en dixièmes de degré.</summary>
    private static double? Temperature(string dumpsys) =>
        Number(dumpsys, TemperaturePattern()) is { } tenths ? tenths / 10.0 : null;

    private static int? Number(string dumpsys, Regex pattern)
    {
        var match = pattern.Match(dumpsys);

        return match.Success
               && int.TryParse(
                   match.Groups[1].Value,
                   NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^\s*level:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LevelPattern();

    [GeneratedRegex(@"^\s*scale:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ScalePattern();

    [GeneratedRegex(@"^\s*status:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"^\s*temperature:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TemperaturePattern();
}

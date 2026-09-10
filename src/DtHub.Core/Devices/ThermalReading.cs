using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// Ce que l'appareil dit de sa chaleur.
///
/// C'est la vraie limite du multicompte sur tablette, et elle est silencieuse
/// comme le refus d'injection : rien n'échoue, tout ralentit. Relevé sur le
/// salon d'entraide d'un produit concurrent, où plusieurs personnes décrivent
/// la même chose : « dès que le SoC dépasse 65 °, il coupe le multitâche et
/// demande d'attendre que ça refroidisse ».
///
/// Le verdict se lit sur <c>Thermal Status</c>, l'échelle de zéro à six que
/// tout Android expose depuis la version 10. Les températures nommées, elles,
/// varient d'un constructeur à l'autre et ne servent qu'au journal : sur le
/// téléphone de référence, le processeur affiche 84 ° alors que l'état vaut
/// zéro et que rien n'est bridé. Un nombre pareil dans un message alarmerait
/// pour rien.
/// </summary>
/// <param name="Status">État thermique, de 0 (rien) à 6 (extinction).</param>
/// <param name="SkinCelsius">Température de surface, pour le journal, si elle est dite.</param>
public sealed partial record ThermalReading(int Status, double? SkinCelsius)
{
    /// <summary>À partir d'ici, Android bride et cela peut se voir.</summary>
    public const int Throttling = 2;

    /// <summary>À partir d'ici, le bridage est lourd.</summary>
    public const int Severe = 3;

    /// <summary>Vrai quand l'appareil bride assez pour que ça se voie.</summary>
    public bool IsThrottling => Status >= Throttling;

    /// <summary>
    /// Lit la sortie de <c>dumpsys thermalservice</c>. Rend <c>null</c> dès que
    /// l'état manque : ne rien savoir de la chaleur est un cas ordinaire, et
    /// l'appelant s'en passe.
    /// </summary>
    public static ThermalReading? Parse(string? dumpsys)
    {
        if (string.IsNullOrWhiteSpace(dumpsys))
        {
            return null;
        }

        var status = StatusPattern().Match(dumpsys);

        if (!status.Success
            || !int.TryParse(status.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return new ThermalReading(value, Skin(dumpsys));
    }

    /// <summary>
    /// Ce qu'il y a à dire au joueur, ou <c>null</c> s'il n'y a rien à dire.
    /// </summary>
    public string? Describe() => Status switch
    {
        >= Severe => Strings.Get("DeviceThrottlingSevere"),
        >= Throttling => Strings.Get("DeviceThrottling"),
        _ => null,
    };

    /// <summary>
    /// Température de surface. Le type 3 est celui de la peau dans l'API
    /// Android : le nom, lui, change d'un constructeur à l'autre.
    ///
    /// La sortie en donne deux, et prendre la première serait faux. Relevé sur
    /// le téléphone de référence : la section « Cached temperatures » annonçait
    /// 48,5 ° quand la section « Current temperatures from HAL » en donnait
    /// 34,4. C'est la seconde qui dit l'instant.
    /// </summary>
    private static double? Skin(string dumpsys)
    {
        var current = dumpsys.IndexOf("Current temperatures", StringComparison.OrdinalIgnoreCase);

        // Une version d'Android qui ne sépare pas les deux sections retombe sur
        // la lecture unique, qui est alors la bonne.
        var skin = SkinPattern().Match(current >= 0 ? dumpsys[current..] : dumpsys);

        return skin.Success
            && double.TryParse(
                skin.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var celsius)
            ? celsius
            : null;
    }

    [GeneratedRegex(@"Thermal Status:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"Temperature\{mValue=(-?[\d.]+),\s*mType=3\b", RegexOptions.IgnoreCase)]
    private static partial Regex SkinPattern();
}

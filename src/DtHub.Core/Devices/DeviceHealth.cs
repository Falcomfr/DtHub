using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>Gravité d'un constat, du plus anodin au plus urgent.</summary>
public enum HealthSeverity
{
    /// <summary>Mérite d'être su, n'empêche rien.</summary>
    Notice,

    /// <summary>Va gêner, et se corrige.</summary>
    Warning,

    /// <summary>Va couper la séance si rien n'est fait.</summary>
    Serious,
}

/// <summary>Un constat sur l'état d'un appareil.</summary>
public readonly record struct HealthFinding(HealthSeverity Severity, string Message);

/// <summary>
/// Le bilan d'un appareil, avant de lancer quoi que ce soit.
///
/// L'application lisait déjà la chaleur, la liaison, et maintenant la batterie
/// et la place libre. **Chacune parlait dans son coin**, et aucune ne parlait
/// avant le lancement : on découvrait le problème une fois les fenêtres
/// ouvertes, c'est-à-dire trop tard pour l'éviter.
///
/// Le bilan les met en file, du plus grave au plus anodin. Il ne décide rien
/// et n'empêche rien : il dit ce qui va gêner, à qui veut le lire.
///
/// **Les messages viennent des lectures elles-mêmes**, pas d'ici. Réécrire ce
/// que <see cref="ThermalReading.Describe" /> dit déjà donnerait deux textes
/// pour un même fait, qui finiraient par diverger.
/// </summary>
public static class DeviceHealth
{
    /// <summary>
    /// Les constats, du plus grave au plus anodin, à gravité égale dans
    /// l'ordre où ils comptent : ce qui coupe la séance, puis ce qui la gêne.
    /// </summary>
    /// <param name="crowdedDevice">
    /// Vrai quand plusieurs comptes sont ouverts sur un appareil qui ne sait
    /// en garder qu'un seul actif.
    /// </param>
    /// <param name="lockedWindows">
    /// Vrai quand les fenêtres de jeu de cet appareil montrent son écran de
    /// verrouillage au lieu du jeu, c'est-à-dire quand son afficheur virtuel
    /// suit le verrouillage et qu'il est verrouillé.
    /// </param>
    public static IReadOnlyList<HealthFinding> Review(
        ThermalReading? heat,
        BatteryReading? battery,
        StorageReading? storage,
        WifiLink? link,
        bool lockedWindows = false,
        bool crowdedDevice = false)
    {
        List<HealthFinding> findings = [];

        // En tête parce que rien d'autre ne compte tant qu'il dure : les
        // fenêtres sont ouvertes et ne montrent pas le jeu. Et ici plutôt
        // qu'au lancement, où le message ne vivait que deux secondes avant
        // que le balayage suivant ne le remplace.
        if (lockedWindows)
        {
            findings.Add(new HealthFinding(HealthSeverity.Serious, Strings.Get("DisplayStaysLocked")));
        }

        // Juste après le cadenas : les fenêtres sont là, elles montrent le
        // jeu, et pourtant une seule est vraiment vivante. C'est le genre de
        // panne qu'on met une soirée à comprendre, parce que rien n'échoue.
        if (crowdedDevice)
        {
            findings.Add(new HealthFinding(HealthSeverity.Serious, Strings.Get("OneAccountAtATime")));
        }

        if (battery?.Describe() is { } power && battery.Concern is { } level)
        {
            findings.Add(new HealthFinding(level, power));
        }

        if (storage?.Describe() is { } room)
        {
            findings.Add(new HealthFinding(
                storage.FreeBytes <= StorageReading.Critical ? HealthSeverity.Serious : HealthSeverity.Warning,
                room));
        }

        if (heat?.Describe() is { } warm)
        {
            findings.Add(new HealthFinding(
                heat.Status >= ThermalReading.Severe ? HealthSeverity.Serious : HealthSeverity.Warning,
                warm));
        }

        // La liaison ne casse rien et se compense déjà toute seule par le
        // tampon vidéo. Elle est dite pour que « ça saccade » ait une réponse,
        // pas pour alarmer : d'où le rang le plus bas.
        if (link is { Is24GHz: true })
        {
            findings.Add(new HealthFinding(HealthSeverity.Notice, Strings.Get("DeviceOn24GHz")));
        }

        return [.. findings.OrderByDescending(f => f.Severity)];
    }

    /// <summary>
    /// Le constat qui parle pour tous, ou <c>null</c> quand tout va bien.
    ///
    /// Un seul : deux avertissements côte à côte dans le même bandeau se
    /// liraient comme un seul, plus long.
    /// </summary>
    public static string? Worst(IReadOnlyList<HealthFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings.Count == 0 ? null : findings[0].Message;
    }
}

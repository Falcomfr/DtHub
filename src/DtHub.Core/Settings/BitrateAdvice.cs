using System.Globalization;

namespace DtHub.Core.Settings;

/// <summary>Ce que vaut un débit, une fois rapporté à ce qu'il doit couvrir.</summary>
public enum BitrateVerdict
{
    /// <summary>L'image se délitera dès que la scène bouge.</summary>
    Insufficient,

    /// <summary>Regardable, mais les mouvements rapides marqueront.</summary>
    Tight,

    /// <summary>Le bon compromis.</summary>
    Comfortable,

    /// <summary>Au-delà de ce que l'oeil distingue : du réseau dépensé pour rien.</summary>
    Generous,
}

/// <summary>
/// Juge un débit à l'aune de ce qu'il doit couvrir : une définition et une
/// cadence.
///
/// Un débit nu ne veut rien dire. Seize mégabits sont généreux en 720p et
/// misérables en 2160p, et c'est précisément l'erreur dans laquelle ce projet
/// est tombé : les paliers de qualité avaient le débit à l'envers, « maximale »
/// recevant cinq fois et demie moins de bits par pixel que « basse ». Personne
/// ne l'avait vu, parce que les nombres pris un à un semblaient tous
/// raisonnables.
///
/// La mesure qui compte est le <b>bit par pixel et par image</b>, celle que
/// l'encodeur reçoit vraiment. Les références publiées par YouTube pour du
/// H.264 de bonne facture tournent toutes autour de 0,10 : 12 Mb/s en 1080p60
/// donnent 0,096, 24 en 1440p60 donnent 0,108, 53 en 2160p60 donnent 0,106.
/// C'est de là que viennent les seuils.
/// </summary>
public static class BitrateAdvice
{
    /// <summary>
    /// Ce que H.265 rend pour un même débit, comparé à H.264.
    ///
    /// À qualité égale, H.265 demande environ un tiers de bits en moins. Sans
    /// en tenir compte, le verdict punirait le bon choix : un utilisateur qui
    /// passe à H.265 verrait son réglage se faire traiter de juste alors qu'il
    /// vient de l'améliorer. La valeur est un ordre de grandeur admis, pas une
    /// mesure faite ici, et elle ne sert qu'à nuancer une appréciation.
    /// </summary>
    public const double Hevc = 0.65;

    private const double Insufficient = 0.050;
    private const double Tight = 0.075;
    private const double Generous = 0.160;

    /// <summary>
    /// Lit un réglage et rend de quoi l'afficher tel quel.
    ///
    /// Les valeurs aberrantes ne lèvent pas : le panneau appelle cette fonction
    /// à chaque frappe, y compris sur un champ à demi effacé.
    /// </summary>
    public static BitrateReading Read(int width, int height, int fps, int kbps, string? codec = null)
    {
        if (width <= 0 || height <= 0 || fps <= 0 || kbps <= 0)
        {
            return new BitrateReading(0, 0, BitrateVerdict.Insufficient, string.Empty);
        }

        var raw = kbps * 1000.0 / ((double)width * height * fps);

        // Rapporté à H.264, qui est l'étalon des seuils : à débit égal, H.265
        // en donne davantage, et le verdict doit le refléter.
        var effective = IsHevc(codec) ? raw / Hevc : raw;

        var verdict = effective switch
        {
            < Insufficient => BitrateVerdict.Insufficient,
            < Tight => BitrateVerdict.Tight,
            <= Generous => BitrateVerdict.Comfortable,
            _ => BitrateVerdict.Generous,
        };

        return new BitrateReading(raw, effective, verdict, Sentence(raw, verdict));
    }

    /// <summary>Mot du verdict, tel qu'il s'affiche.</summary>
    public static string Label(BitrateVerdict verdict) => verdict switch
    {
        BitrateVerdict.Insufficient => "insuffisant",
        BitrateVerdict.Tight => "juste",
        BitrateVerdict.Comfortable => "confortable",
        _ => "généreux",
    };

    private static bool IsHevc(string? codec) =>
        codec is not null && codec.Trim().Equals("h265", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// La phrase montrée sous les réglages. En français explicite : la virgule
    /// décimale n'est pas un détail quand le nombre tient en trois chiffres.
    /// </summary>
    private static string Sentence(double bitsPerPixel, BitrateVerdict verdict) =>
        string.Create(
            CultureInfo.GetCultureInfo("fr-FR"),
            $"{bitsPerPixel:0.000} bit par pixel et par image, {Label(verdict)}");
}

/// <summary>
/// Ce que rend <see cref="BitrateAdvice.Read"/>.
/// </summary>
/// <param name="BitsPerPixel">La mesure brute, telle quelle.</param>
/// <param name="EffectiveBitsPerPixel">
/// La même, ramenée à ce que H.264 aurait demandé pour ce rendu. C'est elle qui
/// décide du verdict.
/// </param>
/// <param name="Verdict">L'appréciation.</param>
/// <param name="Summary">La phrase à afficher, vide si le réglage est incomplet.</param>
public readonly record struct BitrateReading(
    double BitsPerPixel,
    double EffectiveBitsPerPixel,
    BitrateVerdict Verdict,
    string Summary);

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

        return new BitrateReading(raw, effective, Judge(raw, codec), Sentence(raw, Judge(raw, codec)));
    }

    /// <summary>
    /// Ce qu'une finesse choisie donnera vraiment, une fois rapportée à la
    /// définition, à la cadence, et <b>au nombre de fenêtres ouvertes</b>.
    ///
    /// Ce dernier point est propre à cette application, et c'est lui qui
    /// distingue le conseil d'un simple calcul : plusieurs comptes ouverts, ce
    /// sont plusieurs flux sur la même liaison et le même encodeur. Juger un
    /// flux isolé dirait « confortable » pendant que le téléphone s'étrangle.
    /// </summary>
    /// <param name="windows">
    /// Fenêtres ouvertes sur le téléphone. Zéro et une donnent le même compte :
    /// on annonce alors ce que coûtera la première.
    /// </param>
    /// <param name="ceilingKbps">Plafond du profil, qui borne chaque flux.</param>
    public static BitratePlan Plan(
        double bitsPerPixel,
        int width,
        int height,
        int fps,
        string? codec,
        int windows,
        int ceilingKbps)
    {
        if (bitsPerPixel <= 0 || width <= 0 || height <= 0 || fps <= 0)
        {
            return new BitratePlan(BitrateVerdict.Insufficient, 0, 0, string.Empty, string.Empty);
        }

        var pixels = (double)width * height * fps;

        var perWindow = Math.Clamp(
            (int)Math.Round(bitsPerPixel * pixels / 1000.0),
            QualityProfile.FloorKbps,
            Math.Max(QualityProfile.FloorKbps, ceilingKbps));

        var count = Math.Max(1, windows);

        // La finesse servie peut être moindre que celle demandée : le plafond
        // rabote les grandes définitions. C'est celle-là qu'il faut juger, non
        // celle qui a été cochée.
        var served = perWindow * 1000.0 / pixels;
        var verdict = Judge(served, codec);

        return new BitratePlan(
            verdict,
            perWindow,
            perWindow * count,
            Sentence(served, verdict),
            LinkSentence(perWindow, count));
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
    /// Le verdict, une fois la finesse ramenée à ce que H.264 aurait demandé
    /// pour le même rendu : c'est lui l'étalon des seuils.
    /// </summary>
    private static BitrateVerdict Judge(double bitsPerPixel, string? codec) =>
        (IsHevc(codec) ? bitsPerPixel / Hevc : bitsPerPixel) switch
        {
            < Insufficient => BitrateVerdict.Insufficient,
            < Tight => BitrateVerdict.Tight,
            <= Generous => BitrateVerdict.Comfortable,
            _ => BitrateVerdict.Generous,
        };

    /// <summary>
    /// Ce que la liaison recevra. Le total ne paraît qu'à partir de deux
    /// fenêtres : à une seule, le répéter ne dirait rien de plus.
    /// </summary>
    private static string LinkSentence(int perWindowKbps, int windows)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var each = perWindowKbps / 1000.0;

        return windows <= 1
            ? string.Create(fr, $"au plus {each:0.#} Mb/s sur la liaison")
            : string.Create(
                fr,
                $"au plus {each:0.#} Mb/s par fenêtre, {each * windows:0.#} Mb/s à {windows} comptes");
    }

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

/// <summary>
/// Ce que rend <see cref="BitrateAdvice.Plan"/>.
/// </summary>
/// <param name="Verdict">L'appréciation de la finesse réellement servie.</param>
/// <param name="KbpsPerWindow">Le débit demandé pour une fenêtre.</param>
/// <param name="TotalKbps">Ce que toutes les fenêtres demanderont ensemble.</param>
/// <param name="Summary">La finesse et son verdict, à afficher.</param>
/// <param name="LinkSummary">Ce que la liaison recevra, à afficher.</param>
public readonly record struct BitratePlan(
    BitrateVerdict Verdict,
    int KbpsPerWindow,
    int TotalKbps,
    string Summary,
    string LinkSummary);

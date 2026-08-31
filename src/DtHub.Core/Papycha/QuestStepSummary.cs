using System.Text;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Ramène le texte d'une étape à une phrase qu'on lit d'un coup d'œil.
///
/// Le site écrit pour qu'on lise, pas pour qu'on exécute : « Pour lancer la
/// quête, rendez vous au Château d'Amakna en [4,-6] pour parler à Yse Vewibad »
/// tient en une ligne du bandeau, mais on n'y voit ni où aller ni à qui parler.
/// Ce qu'il faut retenir, c'est « Rendez-vous en [4,-6], parlez à Yse Vewibad ».
///
/// Trois recours, du plus sûr au plus grossier :
///
/// 1. les coordonnées et le nom du PNJ, quand le texte les porte ;
/// 2. le verbe d'action et son complément ;
/// 3. la première phrase, débarrassée de ses tournures d'introduction.
///
/// Jamais de vide : une étape sans résumé vaut moins qu'une étape mal résumée,
/// parce qu'elle laisse croire qu'il n'y a rien à faire.
///
/// Fonction pure : elle se vérifie sur des textes enregistrés.
/// </summary>
public static partial class QuestStepSummary
{
    /// <summary>Au-delà, le bandeau tronque : autant couper nous-mêmes, proprement.</summary>
    private const int MaxLength = 110;

    /// <summary>
    /// Résumé d'un texte d'étape.
    /// </summary>
    public static string Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var clean = Whitespace().Replace(text, " ").Trim();

        // Une étape déjà courte et qui commence par un ordre se suffit : la
        // recomposer perdrait ce qu'elle dit de plus. « Rendez-vous en [0,-11],
        // touchez la tombe » vaut mieux que « Rendez-vous en [0,-11] ».
        if (clean.Length <= MaxLength && Instruction().IsMatch(clean))
        {
            return Shortened(clean);
        }

        return Composed(clean) ?? Shortened(clean);
    }

    /// <summary>
    /// Résumé du départ d'une quête, composé de ce que le site en dit dans ses
    /// métadonnées plutôt que dans sa prose.
    ///
    /// Bien plus sûr que la lecture du texte : la position de départ est
    /// renseignée sur 687 quêtes sur 782 et le personnage sur 693. Rend
    /// <c>null</c> quand ni l'une ni l'autre ne l'est.
    /// </summary>
    public static string? OfStart(string? position, string? person)
    {
        var place = Coordinates().Match(position ?? string.Empty);
        var who = (person ?? string.Empty).Trim();

        if (!place.Success && who.Length == 0)
        {
            return null;
        }

        var summary = new StringBuilder();

        if (place.Success)
        {
            summary.Append("Rendez-vous en ").Append(Tidy(place.Value));
        }

        if (who.Length > 0)
        {
            summary.Append(summary.Length > 0 ? ", parlez à " : "Parlez à ").Append(who);
        }

        return summary.Append('.').ToString();
    }

    /// <summary>
    /// Phrase composée à partir de ce que le texte porte de repérable, ou
    /// <c>null</c> quand il n'en porte rien.
    /// </summary>
    private static string? Composed(string text)
    {
        var place = Coordinates().Match(text);
        var who = Person().Match(text);

        if (!place.Success && !who.Success)
        {
            return null;
        }

        var summary = new StringBuilder();

        if (place.Success)
        {
            summary.Append("Rendez-vous en ").Append(Tidy(place.Value));
        }

        if (who.Success)
        {
            var name = TrimConnector(who.Groups["name"].Value);

            if (name.Length > 0)
            {
                summary.Append(summary.Length > 0 ? ", parlez à " : "Parlez à ").Append(name);
            }
        }

        return summary.Length > 0 ? summary.Append('.').ToString() : null;
    }

    /// <summary>
    /// Première phrase, sans sa tournure d'introduction et coupée court.
    ///
    /// « Pour lancer la quête, » et ses semblables n'apprennent rien : la même
    /// formule ouvre des centaines d'étapes.
    /// </summary>
    private static string Shortened(string text)
    {
        var sentence = Sentence().Match(text) is { Success: true } match
            ? match.Groups["first"].Value
            : text;

        sentence = Opener().Replace(sentence, string.Empty).TrimStart();

        if (sentence.Length == 0)
        {
            sentence = text;
        }

        sentence = sentence.TrimEnd(' ', ':', ';', ',');

        if (sentence.Length > MaxLength)
        {
            var cut = sentence.LastIndexOf(' ', MaxLength);

            sentence = (cut > MaxLength / 2 ? sentence[..cut] : sentence[..MaxLength]).TrimEnd()
                + "…";
        }
        else if (!sentence.EndsWith('.') && !sentence.EndsWith('!') && !sentence.EndsWith('?'))
        {
            sentence += ".";
        }

        return Capitalized(sentence);
    }

    /// <summary>
    /// Retire du nom capturé les mots de liaison qu'il a avalés.
    ///
    /// « parlez à Milicien Kâpon en [-1,-12] » donnait « Milicien Kâpon en » :
    /// la capture s'arrête à la ponctuation, pas au sens.
    /// </summary>
    private static string TrimConnector(string name)
    {
        var value = name.Trim().TrimEnd(',', ';', ':', '.', '-', '’', '\'');

        while (true)
        {
            var cut = value.LastIndexOf(' ');

            if (cut < 0)
            {
                break;
            }

            var last = value[(cut + 1)..];

            if (!Connectors.Contains(last, StringComparer.OrdinalIgnoreCase))
            {
                break;
            }

            value = value[..cut].TrimEnd();
        }

        return value;
    }

    /// <summary>Mots qui ne font jamais partie d'un nom propre.</summary>
    private static readonly string[] Connectors =
    [
        "en", "dans", "pour", "puis", "et", "afin", "qui", "que", "au", "aux",
        "sur", "vers", "avec", "de", "du", "des", "à", "a", "le", "la", "les",
    ];

    /// <summary>Coordonnées resserrées : « [ 4 , -6 ] » se lit « [4,-6] ».</summary>
    private static string Tidy(string coordinates) =>
        Whitespace().Replace(coordinates, string.Empty);

    private static string Capitalized(string text) =>
        text.Length > 0 && char.IsLower(text[0])
            ? char.ToUpper(text[0], System.Globalization.CultureInfo.CurrentCulture) + text[1..]
            : text;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\[\s*-?\d+\s*,\s*-?\d+\s*\]")]
    private static partial Regex Coordinates();

    /// <summary>
    /// Nom d'un personnage, reconnu à ce qui l'annonce. Le nom commence par une
    /// majuscule et s'arrête à la ponctuation : sans cette borne, la capture
    /// avalait la fin de la phrase.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:parlez?|parler|voir|aupr[èe]s d[eu]|adressez-vous [àa]|rendre compte [àa])\s+"
        + @"(?:au |à la |aux |à |le |la |les |l['’])?"
        + @"(?<name>\p{Lu}[\p{L}\p{M}'’\- ]{1,40})",
        RegexOptions.IgnoreCase)]
    private static partial Regex Person();

    /// <summary>
    /// Texte qui commence par un ordre donné au lecteur. C'est la marque d'une
    /// étape déjà écrite comme une consigne.
    /// </summary>
    [GeneratedRegex(
        @"^(?:rendez[- ]?vous|allez|retournez|parlez|fouillez|touchez|entrez|sortez"
        + @"|utilisez|ramassez|prenez|tuez|vainquez|r[ée]cup[ée]rez|adressez|choisissez"
        + @"|remettez|donnez|apportez|d[ée]s[ée]quipez|[ée]quipez|pr[ée]sentez|suivez"
        + @"|cherchez|trouvez|inspectez|examinez|attendez|revenez|montez|descendez)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Instruction();

    /// <summary>Première phrase, bornée par un point suivi d'une espace.</summary>
    [GeneratedRegex(@"^(?<first>.+?[.!?])(?:\s|$)")]
    private static partial Regex Sentence();

    /// <summary>
    /// Tournures qui ouvrent une étape sans rien en dire. Relevées sur le site,
    /// pas devinées.
    /// </summary>
    [GeneratedRegex(
        @"^(?:pour\s+(?:lancer|commencer|d[ée]marrer|terminer|finir|ce\s+faire|cela)[^,]{0,40},\s*"
        + @"|cette\s+qu[êe]te\s+(?:consiste|se\s+lance|est)[^,:]{0,60}[,:]\s*"
        + @"|vous\s+devez\s+(?:donc\s+)?"
        + @"|il\s+(?:vous\s+)?(?:faut|suffit)\s+(?:de\s+|d['’])?"
        + @"|ensuite,?\s*|puis,?\s*|enfin,?\s*|alors,?\s*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Opener();
}

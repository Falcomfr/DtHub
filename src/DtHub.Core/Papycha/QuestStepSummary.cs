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
        var who = StartPerson(person);

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
    /// Le personnage de départ, tel qu'on accepte de le nommer.
    ///
    /// La donnée est propre presque partout - sur six cent quatre-vingt-treize
    /// quêtes, deux seulement dépassent six mots - mais elle n'était pas
    /// relue, et « bateau pour vous rendre au village d'Albuera. » donnait
    /// « Parlez à bateau pour vous rendre au village d'Albuera. ».
    ///
    /// Deux garde-fous : la même borne que pour la prose, et la majuscule. Un
    /// personnage porte un nom propre ; « clef secrète des crocs de verre » et
    /// « bateau » n'en sont pas, et il vaut mieux ne rien dire du départ que
    /// d'inviter à parler à un bateau.
    /// </summary>
    private static string StartPerson(string? person)
    {
        var value = Article().Replace((person ?? string.Empty).Trim(), string.Empty);
        var name = NameOf(value);

        return name.Length > 0 && char.IsUpper(name[0]) ? name : string.Empty;
    }

    /// <summary>Article qui précède parfois le nom, « l'Agent de la compagnie ».</summary>
    [GeneratedRegex(@"^(?:l[e]?s?\s+|l['’]|un[e]?\s+|d[eu]\s+|des\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex Article();

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
            var name = NameOf(who.Groups["name"].Value);

            if (name.Length > 0)
            {
                summary.Append(summary.Length > 0 ? ", parlez à " : "Parlez à ").Append(name);
            }
        }

        // La phrase composée passe par la même coupe que l'autre : rien de ce
        // qui sort d'ici ne doit finir au milieu d'un mot.
        return summary.Length > 0 ? Clipped(summary.Append('.').ToString()) : null;
    }

    /// <summary>
    /// Coupe un texte au dernier mot entier qui tient, et le marque d'un point
    /// de suspension. En deçà du plafond, il ressort tel quel.
    /// </summary>
    private static string Clipped(string text)
    {
        if (text.Length <= MaxLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', MaxLength);

        return (cut > MaxLength / 2 ? text[..cut] : text[..MaxLength]).TrimEnd() + "…";
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
            sentence = Clipped(sentence);
        }
        else if (!sentence.EndsWith('.') && !sentence.EndsWith('!') && !sentence.EndsWith('?'))
        {
            sentence += ".";
        }

        return Capitalized(sentence);
    }

    /// <summary>
    /// Le nom, borné au premier mot qui ouvre une autre proposition.
    ///
    /// La capture s'arrête à la ponctuation, et une phrase sans virgule n'en
    /// offre aucune : « auprès du Grand jarl Ordyn et en vous mettant en route »
    /// donnait « Grand jarl Ordyn et en vous mettant en ro », coupé net au
    /// quarantième caractère. Mesuré sur dix-huit captures, seize débordaient
    /// ainsi et onze finissaient au milieu d'un mot.
    ///
    /// Couper au premier mot de rupture les ramène toutes, sans abîmer les noms
    /// qui portent une particule : « Gardien du Donjon de Belladone » n'en
    /// contient aucun.
    /// </summary>
    private static string NameOf(string capture)
    {
        List<string> kept = [];

        foreach (var word in capture.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Breakers.Contains(Bare(word), StringComparer.OrdinalIgnoreCase))
            {
                break;
            }

            kept.Add(word);

            // Aucun personnage du site ne porte plus de six mots : au-delà, la
            // capture a forcément débordé sur autre chose.
            if (kept.Count == MaxNameWords)
            {
                break;
            }
        }

        // Une particule ne termine pas un nom : « Gardien du Donjon de » vient
        // d'une capture coupée trop tard.
        while (kept.Count > 0 && Particles.Contains(Bare(kept[^1]), StringComparer.OrdinalIgnoreCase))
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return string.Join(' ', kept).Trim().TrimEnd(',', ';', ':', '.', '-', '’', '\'');
    }

    private static string Bare(string word) =>
        word.Trim(',', ';', ':', '.', '!', '?', '’', '\'', '-', '(', ')');

    /// <summary>Au-delà, la capture a débordé sur la suite de la phrase.</summary>
    private const int MaxNameWords = 6;

    /// <summary>
    /// Mots qui ne se trouvent jamais au milieu d'un nom propre.
    ///
    /// C'est une classe fermée de la langue - conjonctions, prépositions,
    /// déterminants, pronoms, quelques adverbes de liaison - et non une liste
    /// tirée des cas rencontrés : celle-ci s'allongerait à chaque guide, et le
    /// premier mot oublié rendrait « Truffo lors de votre première visite ».
    ///
    /// Les particules qui appartiennent bel et bien aux noms en sont exclues et
    /// figurent plus bas : « Gardien du Donjon de Belladone » doit survivre.
    /// </summary>
    private static readonly string[] Breakers =
    [
        // Conjonctions
        "et", "ou", "mais", "donc", "or", "ni", "car", "que", "qui", "quoi",
        "quand", "lorsque", "comme", "si", "puisque", "parce",

        // Prépositions
        "à", "a", "en", "dans", "sur", "sous", "vers", "avec", "sans", "pour",
        "par", "chez", "entre", "contre", "depuis", "pendant", "avant", "après",
        "apres", "jusqu", "lors", "malgré", "selon", "près", "hors", "afin",

        // Déterminants
        "un", "une", "ce", "cet", "cette", "ces", "mon", "ma", "mes", "ton",
        "ta", "tes", "son", "sa", "ses", "notre", "nos", "votre", "vos",
        "leur", "leurs", "tout", "toute", "tous", "toutes", "quelques",
        "plusieurs", "aucun", "aucune", "chaque",

        // Pronoms
        "je", "tu", "il", "elle", "on", "nous", "vous", "ils", "elles", "se",
        "y", "lui", "cela", "ça", "ceci", "celui", "celle",

        // Adverbes de liaison
        "ensuite", "puis", "enfin", "alors", "ainsi", "aussi", "encore",
        "déjà", "toujours", "jamais", "plus", "moins", "très", "bien",
    ];

    /// <summary>
    /// Mots qui appartiennent à un nom quand ils sont suivis d'autre chose,
    /// mais qui ne le terminent jamais.
    /// </summary>
    private static readonly string[] Particles =
    [
        "de", "du", "des", "le", "la", "les", "au", "aux", "d", "l",
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
    /// Nom d'un personnage, reconnu à ce qui l'annonce.
    ///
    /// Les amorces sont relevées, pas devinées : sur dix-huit guides, « parlez »
    /// vingt-trois fois, « parlant » quatre, « reparlez » quatre, « parler »
    /// deux, puis « adieux à » et « présentez-vous à ». Trois de ces formes
    /// manquaient, d'où des résumés qui donnaient les coordonnées sans dire à
    /// qui parler.
    ///
    /// Ce qui n'y figure pas n'y figure pas par choix : « vous emmène à
    /// Astrub », « vous déposer au Temple », « vous êtes à Albuera » annoncent
    /// des lieux. Accepter « à » suivi d'une majuscule ferait passer un lieu
    /// pour un personnage.
    ///
    /// La majuscule initiale est explicitement sensible à la casse. Sans cela
    /// elle ne borne rien : en .NET, l'option d'indifférence à la casse
    /// s'applique aussi aux catégories Unicode, et « \p{Lu} » accepte alors les
    /// minuscules. « parlez de nouveau à Waldos » capturait « de nouveau à
    /// Waldos ».
    /// </summary>
    [GeneratedRegex(
        @"\b(?:reparlez|parlez?|parler|parlant|voir|aupr[èe]s d[eu]"
        + @"|adressez-vous [àa]|pr[ée]sentez-vous [àa]|adieux [àa]"
        + @"|rendre compte [àa])\s+"
        + @"(?:(?:de|[àa])\s+nouveau\s+)?"
        + @"(?:au |à la |aux |à |le |la |les |l['’])?"
        + @"(?<name>(?-i:\p{Lu})[\p{L}\p{M}'’\- ]{0,60})",
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

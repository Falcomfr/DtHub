using System.Text;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Brings a step's text down to a sentence that can be read at a glance.
///
/// The site writes for reading, not for executing: "Pour lancer la quête,
/// rendez vous au Château d'Amakna en [4,-6] pour parler à Yse Vewibad" fits
/// on one line of the banner, but it does not show where to go or whom to talk
/// to. What needs to be remembered is "Rendez-vous en [4,-6], parlez à Yse
/// Vewibad".
///
/// Three approaches, from the safest to the crudest:
///
/// 1. the coordinates and the NPC's name, when the text carries them;
/// 2. the action verb and its object;
/// 3. the first sentence, stripped of its introductory turns of phrase.
///
/// Never empty: a step with no summary is worse than a step poorly summarised,
/// because it suggests there is nothing to do.
///
/// Pure function: it is verified against recorded texts.
///
/// <para>
/// **<see cref="Of"/> is no longer wired to the interface.** The step summary
/// was removed from the banner and the list: the page has the full paragraph
/// right above, and the rank is enough to get there. See
/// <see cref="QuestStepLabel"/>. Only <see cref="OfStart"/> is still used, for
/// a quest's start, which does not come from the site's prose but from its
/// metadata. The rest is kept along with its tests rather than dismantled in
/// haste: the two entry points share their machinery, and the sorting deserves
/// to be done separately.
/// </para>
/// </summary>
public static partial class QuestStepSummary
{
    /// <summary>
    /// Beyond this, the banner truncates: better to cut it ourselves, cleanly.
    /// </summary>
    private const int MaxLength = 110;

    /// <summary>
    /// Summary of a step's text.
    /// </summary>
    public static string Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var clean = Whitespace().Replace(text, " ").Trim();

        // A step that is already short and starts with an order is enough on
        // its own: recomposing it would lose what it says beyond that.
        // "Rendez-vous en [0,-11], touchez la tombe" is better than
        // "Rendez-vous en [0,-11]".
        if (clean.Length <= MaxLength && Instruction().IsMatch(clean))
        {
            return Shortened(clean);
        }

        return Composed(clean) ?? Shortened(clean);
    }

    /// <summary>
    /// Summary of a quest's start, composed from what the site says about it
    /// in its metadata rather than in its prose.
    ///
    /// Far more reliable than reading the text: the starting position is given
    /// for 687 quests out of 782 and the character for 693. Returns
    /// <c>null</c> when neither one is.
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
    /// The starting character, as far as it can be safely named.
    ///
    /// The data is clean almost everywhere (out of six hundred and
    /// ninety-three quests, only two exceed six words), but it was not
    /// proofread, and "bateau pour vous rendre au village d'Albuera." used to
    /// give "Parlez à bateau pour vous rendre au village d'Albuera.".
    ///
    /// Two safeguards: the same limit as for the prose, and the capital
    /// letter. A character has a proper name; "clef secrète des crocs de
    /// verre" and "bateau" do not, and it is better to say nothing about the
    /// start than to invite talking to a boat.
    /// </summary>
    private static string StartPerson(string? person)
    {
        var value = Article().Replace((person ?? string.Empty).Trim(), string.Empty);
        var name = NameOf(value);

        return name.Length > 0 && char.IsUpper(name[0]) ? name : string.Empty;
    }

    /// <summary>
    /// Article that sometimes precedes the name, "l'Agent de la compagnie".
    /// </summary>
    [GeneratedRegex(@"^(?:l[e]?s?\s+|l['’]|un[e]?\s+|d[eu]\s+|des\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex Article();

    /// <summary>
    /// Sentence composed from whatever identifiable elements the text carries,
    /// or <c>null</c> when it carries none.
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

        // The composed sentence goes through the same cut as the other one:
        // nothing that comes out of here should end in the middle of a word.
        return summary.Length > 0 ? Clipped(summary.Append('.').ToString()) : null;
    }

    /// <summary>
    /// Cuts a text at the last whole word that fits, and marks it with an
    /// ellipsis. Below the ceiling, it comes back out unchanged.
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
    /// First sentence, without its introductory turn of phrase and cut short.
    ///
    /// "Pour lancer la quête," and its kin teach nothing: the same formula
    /// opens hundreds of steps.
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
    /// The name, bounded by the first word that opens another clause.
    ///
    /// The capture stops at punctuation, and a sentence with no comma offers
    /// none: "auprès du Grand jarl Ordyn et en vous mettant en route" used to
    /// give "Grand jarl Ordyn et en vous mettant en ro", cut clean at the
    /// fortieth character. Measured over eighteen captures, sixteen overran
    /// this way and eleven ended in the middle of a word.
    ///
    /// Cutting at the first breaking word brings all of them back, without
    /// damaging names that carry a particle: "Gardien du Donjon de Belladone"
    /// contains none.
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

            // No character on the site has a name of more than six words:
            // beyond that, the capture must have overrun onto something else.
            if (kept.Count == MaxNameWords)
            {
                break;
            }
        }

        // A particle does not end a name: "Gardien du Donjon de" comes from a
        // capture cut too late.
        while (kept.Count > 0 && Particles.Contains(Bare(kept[^1]), StringComparer.OrdinalIgnoreCase))
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return string.Join(' ', kept).Trim().TrimEnd(',', ';', ':', '.', '-', '’', '\'');
    }

    private static string Bare(string word) =>
        word.Trim(',', ';', ':', '.', '!', '?', '’', '\'', '-', '(', ')');

    /// <summary>
    /// Beyond this, the capture has overrun onto the rest of the sentence.
    /// </summary>
    private const int MaxNameWords = 6;

    /// <summary>
    /// Words that are never found in the middle of a proper name.
    ///
    /// This is a closed class of the language (conjunctions, prepositions,
    /// determiners, pronouns, a few linking adverbs), not a list drawn from
    /// encountered cases: that kind would grow longer with every guide, and
    /// the first forgotten word would give "Truffo lors de votre première
    /// visite".
    ///
    /// Particles that genuinely belong to names are excluded from it and
    /// appear further below: "Gardien du Donjon de Belladone" must survive.
    /// </summary>
    private static readonly string[] Breakers =
    [
        // Conjunctions
        "et", "ou", "mais", "donc", "or", "ni", "car", "que", "qui", "quoi",
        "quand", "lorsque", "comme", "si", "puisque", "parce",

        // Prepositions
        "à", "a", "en", "dans", "sur", "sous", "vers", "avec", "sans", "pour",
        "par", "chez", "entre", "contre", "depuis", "pendant", "avant", "après",
        "apres", "jusqu", "lors", "malgré", "selon", "près", "hors", "afin",

        // Determiners
        "un", "une", "ce", "cet", "cette", "ces", "mon", "ma", "mes", "ton",
        "ta", "tes", "son", "sa", "ses", "notre", "nos", "votre", "vos",
        "leur", "leurs", "tout", "toute", "tous", "toutes", "quelques",
        "plusieurs", "aucun", "aucune", "chaque",

        // Pronouns
        "je", "tu", "il", "elle", "on", "nous", "vous", "ils", "elles", "se",
        "y", "lui", "cela", "ça", "ceci", "celui", "celle",

        // Linking adverbs
        "ensuite", "puis", "enfin", "alors", "ainsi", "aussi", "encore",
        "déjà", "toujours", "jamais", "plus", "moins", "très", "bien",
    ];

    /// <summary>
    /// Words that belong to a name when followed by something else, but that
    /// never end it.
    /// </summary>
    private static readonly string[] Particles =
    [
        "de", "du", "des", "le", "la", "les", "au", "aux", "d", "l",
    ];

    /// <summary>
    /// Tightened coordinates: "[ 4 , -6 ]" reads as "[4,-6]".
    /// </summary>
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
    /// A character's name, recognised by what introduces it.
    ///
    /// The lead-ins were recorded, not guessed: across eighteen guides,
    /// "parlez" twenty-three times, "parlant" four, "reparlez" four, "parler"
    /// two, then "adieux à" and "présentez-vous à". Three of these forms were
    /// missing, hence summaries that gave the coordinates without saying whom
    /// to talk to.
    ///
    /// What is not in it is not in it by choice: "vous emmène à Astrub", "vous
    /// déposer au Temple", "vous êtes à Albuera" announce places. Accepting
    /// "à" followed by a capital letter would pass off a place as a character.
    ///
    /// The initial capital letter is explicitly case-sensitive. Without this
    /// it would bound nothing: in .NET, the case-insensitive option also
    /// applies to Unicode categories, and "\p{Lu}" then accepts lowercase
    /// letters too. "parlez de nouveau à Waldos" used to capture "de nouveau à
    /// Waldos".
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
    /// Text that starts with an order given to the reader. This is the mark of
    /// a step already written as an instruction.
    /// </summary>
    [GeneratedRegex(
        @"^(?:rendez[- ]?vous|allez|retournez|parlez|fouillez|touchez|entrez|sortez"
        + @"|utilisez|ramassez|prenez|tuez|vainquez|r[ée]cup[ée]rez|adressez|choisissez"
        + @"|remettez|donnez|apportez|d[ée]s[ée]quipez|[ée]quipez|pr[ée]sentez|suivez"
        + @"|cherchez|trouvez|inspectez|examinez|attendez|revenez|montez|descendez)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Instruction();

    /// <summary>
    /// First sentence, bounded by a period followed by a space.
    /// </summary>
    [GeneratedRegex(@"^(?<first>.+?[.!?])(?:\s|$)")]
    private static partial Regex Sentence();

    /// <summary>
    /// Turns of phrase that open a step without saying anything. Recorded from
    /// the site, not guessed.
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

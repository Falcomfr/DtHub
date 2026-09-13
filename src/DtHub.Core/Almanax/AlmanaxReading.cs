namespace DtHub.Core.Almanax;

/// <summary>
/// Reading the Almanax page without depending on a language.
///
/// The portal publishes in eight languages and the application speaks
/// three of them. Three phrasings noted on September 10, 2026, on the
/// same day:
///
/// <code>
/// fr  Récupérer 1 Dent de Dragodinde et rapporter l'offrande à Théodoran Ax
/// en  Find 1 Dragoturkey Tooth and take the offering to Antyklime Ax
/// es  Recolectar 1 Diente de dragopavo y llevárselo a Ontoral Zo
/// </code>
///
/// The verb changes, the character changes, down to its name. What
/// does not change is the form: a number, then the object, then a
/// conjunction. It is on this form that we read, not on words to
/// enumerate language by language.
/// </summary>
public static class AlmanaxReading
{
    /// <summary>
    /// The conjunctions that close the object's name, in the
    /// languages the application serves.
    /// </summary>
    private static readonly string[] Connectors = [" et ", " and ", " y "];

    /// <summary>
    /// True when the block read is indeed the DOFUS Touch one.
    ///
    /// This is the safeguard for the whole function. The portal
    /// serves both games on the same page, and DOFUS's Almanax asks
    /// for different objects: nothing on screen would say we are
    /// showing the wrong one. The block's title carries the game's
    /// name in the three languages, in different places:
    ///
    /// <code>
    /// fr  Bonus et Quêtes DOFUS Touch
    /// en  DOFUS Touch bonuses and quests
    /// es  Bonus y misiones DOFUS Touch
    /// </code>
    ///
    /// So we look for the name anywhere, and reject everything else.
    /// Better to show nothing than to show another game's offering.
    /// </summary>
    public static bool IsTouch(string? heading) =>
        (heading ?? string.Empty).Contains("DOFUS Touch", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What follows the colon, or the entire text if there is none.
    ///
    /// The portal prefixes its headings, "Bonus : ", "Bonus: ",
    /// "Quête : ", "Quest: ", "Misión: ". The word changes, the
    /// punctuation too, but the colon is there in all three
    /// languages, and it never appears in the values themselves.
    /// </summary>
    public static string AfterColon(string? text)
    {
        var whole = Clean(text);
        var colon = whole.IndexOf(':', StringComparison.Ordinal);

        return colon < 0 ? whole : whole[(colon + 1)..].Trim();
    }

    /// <summary>
    /// The number and the object to bring back, or <c>null</c> if the
    /// sentence cannot be read.
    ///
    /// Returning <c>null</c> is not a failure: the window then shows
    /// the whole sentence, which already says everything. All we
    /// lose is the highlighting.
    /// </summary>
    public static (int Quantity, string Item)? Offering(string? sentence)
    {
        var whole = Clean(sentence);

        // The first number in the sentence: the verbs all precede
        // it, and no object name starts with a digit.
        var start = -1;
        var end = -1;

        for (var i = 0; i < whole.Length; i++)
        {
            if (!char.IsAsciiDigit(whole[i]))
            {
                continue;
            }

            start = i;
            end = i;

            while (end + 1 < whole.Length && char.IsAsciiDigit(whole[end + 1]))
            {
                end++;
            }

            break;
        }

        if (start < 0 || !int.TryParse(whole[start..(end + 1)], out var quantity))
        {
            return null;
        }

        var rest = whole[(end + 1)..];

        // The closest conjunction, not the first one in the list: an
        // object name may contain "y" or "et" from another language.
        var cut = rest.Length;

        foreach (var connector in Connectors)
        {
            var at = rest.IndexOf(connector, StringComparison.OrdinalIgnoreCase);

            if (at >= 0 && at < cut)
            {
                cut = at;
            }
        }

        var item = rest[..cut].Trim();

        return item.Length == 0 ? null : (quantity, item);
    }

    /// <summary>
    /// The text stripped of the template's blanks: the page renders
    /// its values over several lines, indented, and the raw text
    /// arrives riddled with spaces and line breaks.
    /// </summary>
    public static string Clean(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

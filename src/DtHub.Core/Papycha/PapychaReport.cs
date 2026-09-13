namespace DtHub.Core.Papycha;

/// <summary>
/// What we fill in on the reader's behalf in the site's report form,
/// and nothing more.
///
/// The form asks where the error is. The application puts in it the
/// zone, the quest and its achievement, that is to say what the site
/// itself names. It used to put the step's rank there first; that
/// rank is a numbering that only exists on our side, and so it
/// designated nothing for whoever receives the report.
///
/// The description, for its part, stays empty: it is what the reader
/// saw, and writing it for them would amount to reporting something
/// they did not say.
/// </summary>
public static class PapychaReport
{
    /// <summary>
    /// Length of the "Où se trouve l'erreur ?" (Where is the error?)
    /// field on the site. Beyond that, the browser cuts off the
    /// input and only the beginning would be sent.
    /// </summary>
    public const int MaxLocationLength = 250;

    /// <summary>
    /// The window's breadcrumb chevron, so the landmark reads like
    /// the list where it was found.
    /// </summary>
    private const string Separator = QuestTree.Separator;

    /// <summary>
    /// Where we were reading, in the site's own terms: the zone, the
    /// quest, and the achievement in parentheses.
    ///
    /// Empty when nothing is known: a made-up landmark would be
    /// worth less than the field left blank.
    /// </summary>
    public static string Location(string? zone, string? quest, string? success = null)
    {
        var title = Flatten(quest);
        var rubrique = Flatten(zone);
        var achievement = Flatten(success);

        if (title.Length == 0)
        {
            return Cut(rubrique);
        }

        // The achievement goes with the quest, not the zone: it is
        // the quest it says something about. A page that has none
        // does not show an empty parenthesis.
        if (achievement.Length > 0)
        {
            title += $" ({achievement})";
        }

        return Cut(rubrique.Length == 0 ? title : rubrique + Separator + title);
    }

    /// <summary>
    /// The text on a single line, without edge blanks or duplicates.
    /// </summary>
    private static string Flatten(string? text) =>
        string.Join(
            ' ',
            (text ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Brought down to what the field accepts. This case should not
    /// occur, a zone name and a quest title fitting well within it;
    /// it is a safeguard against input truncated by the browser.
    /// </summary>
    private static string Cut(string text) =>
        text.Length <= MaxLocationLength ? text : text[..MaxLocationLength].TrimEnd();
}

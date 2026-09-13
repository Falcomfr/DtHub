namespace DtHub.Core.Papycha;

/// <summary>
/// What is written next to a step's rank, and most often nothing.
///
/// Every step used to be summarized, in the two places that show it:
/// the banner and the list where its rank is picked. A quest guide
/// gained from this a line of truncated prose under the rank, which
/// nobody read since the page has it in full just above. The rank
/// alone is enough to get there.
///
/// What remains is what is not prose and that truly situates: a
/// quest's departure, and the section titles of a place sheet.
///
/// Here rather than in the window, for the same reason as
/// <c>StartupPresence</c>: a display decision is easier to verify
/// when it depends on nothing.
/// </summary>
public static class QuestStepLabel
{
    /// <summary>
    /// The label of a step.
    /// </summary>
    /// <param name="step">
    /// The step and its nature, as the bridge reports them.
    /// </param>
    /// <param name="isDeparture">
    /// True for the first step of a page that starts with a
    /// departure, which is what the bridge says. Without this
    /// reservation, the departure ended up announced above the first
    /// paragraph of guides that have none.
    /// </param>
    /// <param name="departure">
    /// The departure built from the site's metadata, position and
    /// character, more reliable than its prose. Empty when the site
    /// does not provide them.
    /// </param>
    /// <summary>
    /// True when this step is the launch of the quest and not an
    /// instruction.
    ///
    /// The bridge announces a departure as soon as the page carries a
    /// departure block **or** it is a place sheet; a section title is
    /// never one. This is the same reservation as the label's, and
    /// the two must share it, or a page would end up with a
    /// departure shown above a title.
    /// </summary>
    public static bool IsDeparture(QuestStep step, bool isFirst, bool startsAtDeparture) =>
        isFirst && startsAtDeparture && !step.IsTitle;

    /// <summary>
    /// The rank of a step and the number of steps, the departure set
    /// aside.
    ///
    /// The departure is not a step of the journey: it is the place
    /// one goes to start it. Counting it gave "Step 1 / 2" to a guide
    /// that has only one instruction, which is the case for one
    /// hundred and eighty-two of the site's seven hundred and
    /// eighty-two guides, and "Step 1 / 1" to thirty others that have
    /// none.
    /// </summary>
    /// <param name="index">
    /// Rank of the step among those the bridge returned.
    /// </param>
    /// <param name="count">
    /// Number of steps returned, departure included.
    /// </param>
    /// <param name="hasDeparture">
    /// True when the first of them is the departure.
    /// </param>
    /// <returns>
    /// The rank to show and the total, or <c>null</c> for the
    /// departure, which is named instead of numbered.
    /// </returns>
    public static (int Rank, int Total)? Numbering(int index, int count, bool hasDeparture)
    {
        var first = hasDeparture ? 1 : 0;

        if (index < first || index >= count)
        {
            return null;
        }

        return (index - first + 1, count - first);
    }

    public static string For(QuestStep step, bool isDeparture, string? departure)
    {
        // The title comes before the departure, and the reverse
        // order was a bug.
        //
        // The bridge announces a departure as soon as the page
        // carries a departure block **or** it is a place sheet, but
        // it only pushes a departure step in the second case. A page
        // that has both, section titles and a departure block,
        // therefore saw its first title replaced by the departure
        // line. Found across the site's seven hundred and
        // eighty-two guides: two are in this case, "La voie du
        // Wukang / La voie du Wukin" and "L'éternelle moisson",
        // whose first title is "Liste des Monstres".
        //
        // A title is rendered as is. Summarizing it added a capital
        // letter and a final period it had not asked for: "Les
        // salles" displayed as "Les salles."
        if (step.IsTitle)
        {
            return step.Text;
        }

        return isDeparture ? departure ?? string.Empty : string.Empty;
    }
}

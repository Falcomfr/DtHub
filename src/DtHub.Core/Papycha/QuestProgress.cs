namespace DtHub.Core.Papycha;

/// <summary>
/// The rank and the total shown at the foot of a guide.
///
/// **The site publishes this count, and it is the site that is
/// right.** Each quest page carries it in its intro block, under
/// "Progression": "Étape 9/11". The window used to compute its own
/// instead, by counting the catalog's quests that name the same
/// achievement, and those two numbers are not the same thing. The
/// "Succès associé" field says which achievement a quest belongs
/// to; it does not say how many quests that achievement holds, and
/// the catalog only ever holds the ones it has indexed.
///
/// Measured against the site over the one hundred and fifteen
/// achievements the catalog knows: six totals were right.
/// Twenty-eight were too high, "Le théâtre des gobelins" counting
/// six quests for three and "Intérimaire frigostien" nineteen for
/// three; seventy-five were too low, "Devenir une légende" counting
/// two for twelve. "À la barbe du roi" announced itself as the
/// third of five where the site announces it as the ninth of
/// eleven.
///
/// A quest that belongs to no achievement is one of one. The site
/// then publishes no progression at all, and the foot of the window
/// used to show nothing: a lone quest is still a quest, and saying
/// so is what distinguishes it from a quest whose sequence we have
/// failed to find.
///
/// Here rather than in the window, for the same reason as
/// <see cref="QuestStepLabel"/> and <see cref="QuestNeighbourhood"/>:
/// the test project targets net10.0 while the application targets
/// net10.0-windows, so a rule left in the window is a rule nothing
/// proves.
/// </summary>
public static class QuestProgress
{
    /// <summary>
    /// What to show, or <c>null</c> to show nothing.
    /// </summary>
    /// <param name="published">
    /// What the page announced, or <c>null</c> as long as it has not
    /// arrived. The window shows the neighbours before the page
    /// loads, so that the buttons answer during the wait; the count
    /// follows the same order, provisional then settled.
    /// </param>
    /// <param name="hasSuccess">
    /// True when the quest belongs to an achievement, from the
    /// catalog or from the page.
    /// </param>
    /// <param name="known">
    /// What the catalog worked out, kept for the wait and for the
    /// six achievements whose pages publish no progression.
    /// </param>
    public static (int Rank, int Total)? Of(
        QuestFacts? published,
        bool hasSuccess,
        QuestNeighbours known)
    {
        // Both numbers, or neither: a rank without a total says
        // nothing, and the site gives them in the same sentence.
        if (published is { StepNumber: > 0, StepCount: > 0 })
        {
            // "Étape 9/3" would be the site contradicting itself. It
            // does not today, and printing a rank above its total is
            // the one thing this display must never do.
            return (published.StepNumber, Math.Max(published.StepCount, published.StepNumber));
        }

        if (!hasSuccess)
        {
            return published is null ? null : (1, 1);
        }

        return known.Count > 0 ? (known.Rank, known.Count) : null;
    }
}

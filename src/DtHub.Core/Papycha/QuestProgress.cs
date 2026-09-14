namespace DtHub.Core.Papycha;

/// <summary>
/// The rank and the total shown at the foot of a guide.
///
/// **It counts the achievement's quests, the ones the window lists,
/// and nothing else.** The list shows "Devenir une légende (2)" with
/// its two quests; the foot of the window must therefore read 1 / 2
/// on the first of them. What the site publishes under
/// "Progression" counts something else: its own achievement holds
/// twelve quests, ten of which the catalogue does not carry, and
/// announcing "10 / 12" under a list of two was answering a question
/// nobody asked.
///
/// D159 got this backwards and made the site authoritative. This
/// replaces that rule.
///
/// A quest that belongs to no achievement is one of one. It used to
/// show nothing at all, which is what started this: one can still
/// walk to the next quest from there, and the counter says one of
/// one because that is what the sequence holds, not because there is
/// nothing after it.
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
    /// What to show at the foot of a guide.
    /// </summary>
    /// <param name="known">
    /// What the catalogue worked out: the quest's rank within its
    /// achievement, and how many quests that achievement holds.
    /// </param>
    /// <returns>
    /// The rank and the total. Never nothing: a quest is always at
    /// least one of one.
    /// </returns>
    public static (int Rank, int Total) Of(QuestNeighbours known) =>
        known is { Count: > 0, Rank: > 0 } ? (known.Rank, known.Count) : (1, 1);
}

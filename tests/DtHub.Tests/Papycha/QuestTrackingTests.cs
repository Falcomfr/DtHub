using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>The pure rules that lay the player's tracker over the guides list.</summary>
public class QuestTrackingTests
{
    private static readonly TrackerProgress Progress = new(
        new Dictionary<int, QuestStatus>
        {
            [1] = QuestStatus.InProgress,
            [2] = QuestStatus.Blocked,
            [3] = QuestStatus.Ignored,
            [4] = QuestStatus.Completed,
        },
        new HashSet<int> { 4, 5 },
        new HashSet<int> { 1, 6 },
        []);

    [Theory]
    [InlineData(1, QuestStatus.InProgress)]
    [InlineData(2, QuestStatus.Blocked)]
    [InlineData(3, QuestStatus.Ignored)]
    [InlineData(4, QuestStatus.Completed)]
    [InlineData(5, QuestStatus.Completed)]
    [InlineData(99, QuestStatus.NotStarted)]
    public void StatusOf_prefers_completed_then_explicit(int id, QuestStatus expected) =>
        Assert.Equal(expected, QuestTracking.StatusOf(Progress, id));

    [Fact]
    public void StatusOf_is_not_started_without_progress() =>
        Assert.Equal(QuestStatus.NotStarted, QuestTracking.StatusOf(null, 1));

    private static QuestSummary Named(int id, int order = 0, params string[] needs) =>
        new() { Id = id, Title = "Q" + id, Url = "q" + id, PlayOrder = order, Prerequisites = needs };

    [Fact]
    public void NextQuests_keeps_the_site_s_list_and_order_when_it_gives_one()
    {
        var progress = Progress with { Next = [6, 1, 6] };

        Assert.Equal([6, 1], QuestTracking.NextQuests(progress, [Named(1), Named(6)]).Select(q => q.Id));
    }

    /// <summary>
    /// The site fills nextQuests for its active character only: for the
    /// others "Suivantes" comes from the previous quests done.
    /// </summary>
    [Fact]
    public void NextQuests_follow_what_is_done_when_the_site_gives_none()
    {
        // Q4 and Q5 are completed, Q3 ignored, Q1 in progress.
        var catalog = new[]
        {
            Named(4),
            Named(5),
            Named(10, 0, "Q4"), // previous done, untouched: next
            Named(11, 0, "Q4", "Q12"), // one previous not done
            Named(12), // no previous quest: not a follow-up
            Named(13, 0, "Q4", "Q5"), // every previous done: next, played before Q10
            Named(3, 0, "Q4"), // ignored
            Named(1, 0, "Q5"), // in progress: still next
            Named(14, 0, "Alignement bontarien 40 minimum."), // names no quest
            Named(15, 0, "Q15"), // only itself
        };

        Assert.Equal(
            [1, 10, 13],
            QuestTracking.NextQuests(Progress, catalog).Select(q => q.Id).Order());
    }

    [Fact]
    public void NextQuests_come_in_play_order()
    {
        var catalog = new[] { Named(4), Named(20, 3, "Q4"), Named(21, 1, "Q4"), Named(22, 2, "Q4") };

        Assert.Equal([21, 22, 20], QuestTracking.NextQuests(Progress, catalog).Select(q => q.Id));
    }

    [Fact]
    public void NextQuests_is_empty_without_progress() =>
        Assert.Empty(QuestTracking.NextQuests(null, [Named(1)]));

    [Fact]
    public void Passes_to_do_drops_completed_and_ignored() =>
        Assert.Equal([1, 2, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, true, false, false)));

    [Fact]
    public void Passes_available_keeps_available_only() =>
        Assert.Equal([1, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, false, true, false)));

    [Fact]
    public void Passes_both_filters_combine() =>
        Assert.Equal([1, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, true, true, false)));

    [Fact]
    public void Passes_available_also_keeps_what_is_startable() =>
        Assert.Equal([1, 3, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, false, true, i == 3)));

    [Fact]
    public void Passes_is_always_true_without_progress() =>
        Assert.True(QuestTracking.Passes(null, 1, true, true, false));

    [Theory]
    [InlineData(QuestStatus.NotStarted, 5, 1, 0, true)]
    [InlineData(QuestStatus.NotStarted, 5, 0, 0, false)]
    [InlineData(QuestStatus.NotStarted, 5, 3, 3, false)]
    [InlineData(QuestStatus.NotStarted, 5, 4, 3, true)]
    [InlineData(QuestStatus.NotStarted, 1, 1, 0, false)]
    [InlineData(QuestStatus.InProgress, 5, 1, 0, false)]
    [InlineData(QuestStatus.Completed, 5, 1, 0, false)]
    [InlineData(QuestStatus.Blocked, 5, 1, 0, false)]
    [InlineData(QuestStatus.Ignored, 5, 1, 0, false)]
    public void ShouldStart_only_for_a_fresh_move_past_the_first_step(
        QuestStatus status, int count, int index, int resumed, bool expected) =>
        Assert.Equal(expected, QuestTracking.ShouldStart(status, count, index, resumed));

    [Theory]
    [InlineData(5, 4, true)]
    [InlineData(5, 3, false)]
    [InlineData(0, 0, false)]
    public void IsLastStep_matches_the_final_index(int count, int index, bool expected) =>
        Assert.Equal(expected, QuestTracking.IsLastStep(count, index));

    private static readonly TrackerCharacter[] Characters =
    [
        new("a", "A", 1), new("b", "B", 2), new("c", "C", 3),
    ];

    [Theory]
    [InlineData("b", "a", "b")]
    [InlineData("zz", "a", "a")]
    [InlineData(null, null, "a")]
    [InlineData("zz", "yy", "a")]
    public void PickCharacter_falls_back_remembered_then_active_then_first(
        string? remembered, string? active, string expected) =>
        Assert.Equal(expected, QuestTracking.PickCharacter(Characters, remembered, active)?.Id);

    [Fact]
    public void PickCharacter_is_null_for_no_character() =>
        Assert.Null(QuestTracking.PickCharacter([], "a", "a"));

    private static TrackerProgress Done(params int[] ids) =>
        new(new Dictionary<int, QuestStatus>(), new HashSet<int>(ids), new HashSet<int>(), []);

    private static readonly TrackerState Account = new(
        "n",
        [new("a", "Falcom", 190), new("b", "Mule", 50), new("c", "Sacri", 120)],
        "a",
        new Dictionary<string, TrackerProgress>
        {
            ["a"] = Done(),
            ["b"] = Done(7),
            ["c"] = Done(),
        });

    [Fact]
    public void OthersToFinish_offers_the_other_characters_not_done_yet() =>
        Assert.Equal(["Sacri"], QuestTracking.OthersToFinish(Account, "a", 7).Select(c => c.Name));

    [Fact]
    public void OthersToFinish_keeps_the_account_order() =>
        Assert.Equal(["Mule", "Sacri"], QuestTracking.OthersToFinish(Account, "a", 8).Select(c => c.Name));

    [Fact]
    public void OthersToFinish_is_empty_with_one_character() =>
        Assert.Empty(QuestTracking.OthersToFinish(
            Account with { Characters = [new("a", "Falcom", 190)] }, "a", 8));

    private static readonly TrackerProgress Zone = new(
        new Dictionary<int, QuestStatus>
        {
            [1] = QuestStatus.InProgress,
            [2] = QuestStatus.InProgress,
            [3] = QuestStatus.Ignored,
        },
        new HashSet<int> { 4, 5 },
        new HashSet<int>(),
        []);

    private static QuestSummary Q(int id, QuestRecurrence recurrence = QuestRecurrence.Once, params int[] sections) =>
        new() { Id = id, Title = "Q", Recurrence = recurrence, SectionIds = sections };

    [Fact]
    public void ZoneProgress_counts_done_over_total_and_what_is_started()
    {
        var tally = QuestTracking.ZoneProgress(Zone, [Q(1), Q(2), Q(3), Q(4), Q(5), Q(6)]);

        Assert.Equal(new ZoneTally(2, 6, 2), tally);
        Assert.Equal("2/6", tally?.Count);
        Assert.False(tally?.IsComplete);
        Assert.StartsWith("2 ", tally?.StartedTip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(QuestRecurrence.Daily)]
    [InlineData(QuestRecurrence.Weekly)]
    [InlineData(QuestRecurrence.Repeatable)]
    [InlineData(QuestRecurrence.Seasonal)]
    public void ZoneProgress_leaves_quests_done_again_out_of_the_count(QuestRecurrence recurrence)
    {
        var tally = QuestTracking.ZoneProgress(Zone, [Q(4), Q(5, QuestRecurrence.Unknown), Q(6, recurrence)]);

        Assert.Equal(new ZoneTally(2, 2, 0), tally);
        Assert.True(tally?.IsComplete);
    }

    [Fact]
    public void ZoneProgress_leaves_out_the_site_s_repeatable_section_too()
    {
        // Same rule as the row's glyph: the site files it among repeatables
        // without a rhythm.
        var tally = QuestTracking.ZoneProgress(
            Zone, [Q(4), Q(6, QuestRecurrence.Unknown, QuestRecurrenceLabel.RepeatableSection)]);

        Assert.Equal(new ZoneTally(1, 1, 0), tally);
    }

    [Fact]
    public void ZoneProgress_still_counts_a_repeatable_in_progress() =>
        Assert.Equal(1, QuestTracking.ZoneProgress(Zone, [Q(1, QuestRecurrence.Daily), Q(4)])?.Started);

    [Fact]
    public void ZoneProgress_of_repeatables_only_is_never_complete() =>
        Assert.False(QuestTracking.ZoneProgress(Zone, [Q(4, QuestRecurrence.Daily)])?.IsComplete);

    [Fact]
    public void ZoneProgress_is_null_without_a_tracker() =>
        Assert.Null(QuestTracking.ZoneProgress(null, [Q(1), Q(2)]));
}

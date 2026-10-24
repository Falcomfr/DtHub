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

    [Fact]
    public void NextQuests_from_the_site_keep_only_startable_quests()
    {
        // 6 is listed and follows nothing; 7 is listed but its only previous, 8, is not done ("Tour de l'île").
        var progress = Progress with { Next = [7, 6], Available = new HashSet<int> { 6, 7 } };

        Assert.Equal([6], QuestTracking.NextQuests(progress, [Named(6), Named(7, 0, "Q8"), Named(8)]).Select(q => q.Id));
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
    public void NextQuests_read_from_what_is_done_leave_repeatable_quests_out()
    {
        var catalog = new[]
        {
            Named(4),
            Named(30, 0, "Q4"),
            Named(31, 0, "Q4") with { Recurrence = QuestRecurrence.Daily },
            Named(32, 0, "Q4") with { SectionIds = [QuestRecurrenceLabel.RepeatableSection] },
        };

        Assert.Equal([30], QuestTracking.NextQuests(Progress, catalog).Select(q => q.Id));
    }

    [Fact]
    public void NextQuests_read_from_what_is_done_leave_blocked_and_too_high_quests_out()
    {
        var progress = Progress with { Statuses = new Dictionary<int, QuestStatus>(Progress.Statuses) { [40] = QuestStatus.Blocked } };
        var catalog = new[] { Named(4), Named(40, 0, "Q4"), Named(41, 0, "Q4"), Named(42, 0, "Q4"), Named(43, 0, "Q4") };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [41] = new([new TouchStepReward(50, 1f, 1f, 0f, false, [])], []),
            [42] = new([new TouchStepReward(20, 1f, 1f, 0f, false, [])], []),
        };

        // Quest 43 has no facts read: its level is not known, it stays.
        Assert.Equal([42, 43], QuestTracking.NextQuests(progress, catalog, facts, 20).Select(q => q.Id).Order());
        Assert.Equal([41, 42, 43], QuestTracking.NextQuests(progress, catalog, facts, 0).Select(q => q.Id).Order());
    }

    [Fact]
    public void NextQuests_leave_out_a_ruled_out_way_and_follow_either_alternative()
    {
        // Two ways: 4 (done) then 35, or 30 then 31. 32, on no way, follows 33, the alternative of done 4.
        var catalog = new[]
        {
            Named(4) with { Alternatives = [30] }, Named(30) with { Alternatives = [4] }, Named(31, 0, "Q30") with { Alternatives = [35] },
            Named(35, 0, "Q4") with { Alternatives = [31] }, Named(32, 0, "Q33"), Named(33) with { Alternatives = [4] }, Named(34, 0, "Q4"),
        };
        var chain = new QuestChainIndex(catalog);
        QuestNode Node(QuestSummary q) => new(QuestNodeKind.Quest, q.Title, Quest: q, Needs: [.. q.Prerequisites.Select(n => new QuestNeed(n, chain.Find(n)))]);

        Assert.Equal([32, 34, 35], QuestTracking.NextQuests(Progress, catalog).Select(q => q.Id).Order());
        // From the site's own list too.
        Assert.Equal([34], QuestTracking.NextQuests(Progress with { Next = [30, 34], Available = new HashSet<int> { 30, 34 } }, catalog).Select(q => q.Id));
        // The lock: met, the need checked through its alternative.
        var locked = QuestTracking.Locked(Node(catalog[4]), Progress with { Available = new HashSet<int>() }, chain);
        Assert.True(locked.NeedsMet);
        Assert.True(Assert.Single(locked.Needs!).Met);
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
    public void Passes_doable_drops_completed_and_ignored_and_keeps_blocked() =>
        Assert.Equal([1, 2, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(
            Progress with { Available = new HashSet<int>() }, i, true, i is 2 or 3 or 4 or 6)));

    [Fact]
    public void Passes_doable_drops_a_listed_quest_that_is_not_startable() =>
        Assert.Equal([1], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, true, i != 6)));

    [Fact]
    public void Passes_doable_keeps_a_quest_in_progress_for_every_character()
    {
        // Below its optimal level, repeatable or without facts, DT Hub's reading says no; D219 keeps it.
        Assert.True(QuestTracking.Passes(Progress with { Available = new HashSet<int>() }, 1, true, false));
        // The site's list for its active character may leave it out.
        Assert.True(QuestTracking.Passes(Progress with { Available = new HashSet<int> { 6 } }, 1, true, false));
    }

    [Fact]
    public void Passes_doable_trusts_the_site_s_list_for_its_active_character() =>
        Assert.Equal([1, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, true, i is 1 or 2 or 3 or 6)));

    [Fact]
    public void Passes_keeps_everything_with_the_filter_off() =>
        Assert.Equal([1, 2, 3, 4, 5, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, false, false)));

    [Fact]
    public void Passes_is_always_true_without_progress() =>
        Assert.True(QuestTracking.Passes(null, 1, true, false));

    [Fact]
    public void Doable_keeps_a_blocked_quest_that_is_otherwise_startable()
    {
        var catalog = new[] { Named(1), Named(2, 0, "Q1") };
        var chain = new QuestChainIndex(catalog);
        var facts = new TouchQuestFacts([new TouchStepReward(10, 1f, 1f, 0f, false, [])], []);
        var blocked = new TrackerProgress(
            new Dictionary<int, QuestStatus> { [2] = QuestStatus.Blocked }, new HashSet<int> { 1 }, new HashSet<int>(), []);

        Assert.True(QuestTracking.Doable(catalog[1], blocked, facts, 10, chain));
        // Still the other rules: level, previous quests.
        Assert.False(QuestTracking.Doable(catalog[1], blocked, facts, 9, chain));
        Assert.False(QuestTracking.Doable(catalog[1], blocked with { Completed = new HashSet<int>() }, facts, 10, chain));
        // Facts not read: unknown, not doable.
        Assert.False(QuestTracking.Doable(catalog[1], blocked, null, 10, chain));
        // The site's list decides for its active character.
        Assert.True(QuestTracking.Doable(catalog[1], blocked with { Available = new HashSet<int> { 2 } }, null, 10, chain));
    }

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

    private static readonly QuestSummary[] Six = [Q(1), Q(2), Q(3), Q(4), Q(5), Q(6), Q(7, QuestRecurrence.Daily)];

    [Fact]
    public void ZoneProgress_under_doable_counts_what_the_filter_keeps_as_the_label_does()
    {
        // 3 is ignored: left out though startable; 2 is in progress: kept though not; the daily 7 counts, as in the
        // zone's label.
        var tally = QuestTracking.ZoneProgress(Zone, Six, doable: true, startable: q => q.Id is 1 or 3 or 6 or 7);
        var one = QuestTracking.ZoneProgress(Zone with { Statuses = new Dictionary<int, QuestStatus>() }, Six, doable: true, startable: q => q.Id == 6);

        Assert.Equal("4", tally?.Count);
        Assert.Equal("4 quêtes faisables", tally?.PassingTip);
        Assert.Equal(2, tally?.Started);
        Assert.False(tally?.IsComplete);
        Assert.Equal("1 quête faisable", one?.PassingTip);
    }

    [Fact]
    public void ZoneProgress_without_a_filter_keeps_done_over_total_and_no_tip()
    {
        var tally = QuestTracking.ZoneProgress(Zone, [Q(4), Q(5)]);

        Assert.Equal("2/2", tally?.Count);
        Assert.True(tally?.IsComplete);
        Assert.Null(tally?.PassingTip);
    }
    private static QuestNode Row(QuestSummary quest, QuestChainIndex chain) =>
        new(QuestNodeKind.Quest, quest.Title, Quest: quest, Needs: [.. quest.Prerequisites.Select(n => new QuestNeed(n, chain.Find(n)))]);

    private static TrackerProgress Done(IEnumerable<int> completed, Dictionary<int, QuestStatus>? statuses = null) =>
        new(statuses ?? [], completed.ToHashSet(), new HashSet<int>(), []);

    private static readonly QuestSummary Needy = Named(12, 0, "Q10", "Q11", "Un objet");

    private static readonly QuestChainIndex NeedyChain = new([Named(10), Named(11), Needy]);

    [Fact]
    public void The_lock_is_met_when_every_quest_prerequisite_is_done_and_checks_each()
    {
        var row = QuestTracking.Locked(Row(Needy, NeedyChain), Done([10, 11]), NeedyChain);

        Assert.True(row.NeedsMet);
        // Free text has no mark.
        Assert.Equal([true, true, null], row.Needs!.Select(n => n.Met));
    }

    [Fact]
    public void The_lock_is_unmet_while_a_quest_prerequisite_is_not_done_and_marks_it()
    {
        var row = QuestTracking.Locked(Row(Needy, NeedyChain), Done([10]), NeedyChain);

        Assert.False(row.NeedsMet);
        Assert.Equal([true, false, null], row.Needs!.Select(n => n.Met));
    }

    [Theory]
    [InlineData(QuestStatus.InProgress)]
    [InlineData(QuestStatus.Completed)]
    public void The_lock_is_met_on_a_quest_in_progress_or_done_and_marks_no_missing_one(QuestStatus status)
    {
        // "Terre à terre" in progress, "Tour de l'île" not ticked: it was doable, the lock must not say otherwise.
        var row = QuestTracking.Locked(Row(Needy, NeedyChain), Done([10], new() { [12] = status }), NeedyChain);

        Assert.True(row.NeedsMet);
        Assert.Equal([true, null, null], row.Needs!.Select(n => n.Met));
    }

    [Fact]
    public void A_listed_quest_with_one_previous_done_of_two_has_a_met_lock_and_no_missing_mark()
    {
        // "La fatalité": "Cliquetis fou." or "Entraînement avec Tarche", one per alignment.
        var row = QuestTracking.Locked(Row(Needy, NeedyChain), Done([10]) with { Available = new HashSet<int> { 12 } }, NeedyChain);

        Assert.True(row.NeedsMet);
        Assert.Equal([true, null, null], row.Needs!.Select(n => n.Met));
    }

    [Fact]
    public void A_quest_naming_no_other_quest_keeps_a_neutral_lock_and_its_own_name_no_mark()
    {
        var self = Named(20, 0, "Q20", "Un objet");
        var chain = new QuestChainIndex([self]);

        var row = QuestTracking.Locked(Row(self, chain), Done([]), chain);
        Assert.Null(row.NeedsMet);
        Assert.All(row.Needs!, n => Assert.Null(n.Met));

        var started = QuestTracking.Locked(Row(self, chain), Done([], new() { [20] = QuestStatus.InProgress }), chain);
        Assert.True(started.NeedsMet);
        Assert.All(started.Needs!, n => Assert.Null(n.Met));
    }

    [Fact]
    public void Without_a_tracker_the_lock_stays_neutral()
    {
        var row = QuestTracking.Locked(Row(Needy, NeedyChain), null, NeedyChain);

        Assert.Null(row.NeedsMet);
        Assert.All(row.Needs!, n => Assert.Null(n.Met));
    }
}

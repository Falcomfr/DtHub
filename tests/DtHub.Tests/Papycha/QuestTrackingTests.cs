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

    [Fact]
    public void Passes_to_do_drops_completed_and_ignored() =>
        Assert.Equal([1, 2, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, true, false)));

    [Fact]
    public void Passes_available_keeps_available_only() =>
        Assert.Equal([1, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, false, true)));

    [Fact]
    public void Passes_both_filters_combine() =>
        Assert.Equal([1, 6], Enumerable.Range(1, 6).Where(i => QuestTracking.Passes(Progress, i, true, true)));

    [Fact]
    public void Passes_is_always_true_without_progress() =>
        Assert.True(QuestTracking.Passes(null, 1, true, true));

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
}

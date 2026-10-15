using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// Fixtures are cut down from the tracker page captured on 2026-10-14,
/// kept inline so the tests stay off the network.
/// </summary>
public class TrackerPageParserTests
{
    private const string Linked = """
        <script>var x = 1;</script>
        <script>window.PAPYCHA_QUEST_TRACKER = {"ajaxUrl":"https:\/\/papycha.fr\/wp-admin\/admin-ajax.php","nonce":"26794092b6",
        "space":{"active_character_id":"b","characters":[
          {"id":"a","name":"Vieux","level":50,"archived":true,"quest_statuses":[]},
          {"id":"b","name":"Falcom","level":190,"archived":false,"quest_statuses":{"3692":"completed","47974":"in_progress","100":"weird"}}]},
        "views":{"b":{"progress":{"completed_quests":[3692,3600],"available_quests":[47974,3916],"inferred_quests":[3600]},
          "nextQuests":[3916,{"id":3931,"name":"La potion"},"x"]}},
        "catalog":{"quests":[]}};</script>
        """;

    [Fact]
    public void Linked_page_gives_state()
    {
        var result = TrackerPageParser.Parse(Linked);

        Assert.Equal(TrackerReadOutcome.Linked, result.Outcome);
        var state = result.State!;
        Assert.Equal("26794092b6", state.Nonce);
        Assert.Equal(new TrackerCharacter("b", "Falcom", 190), Assert.Single(state.Characters));
        Assert.Equal("b", state.ActiveCharacterId);

        var progress = state.Progress["b"];
        Assert.Equal(QuestStatus.Completed, progress.Statuses[3692]);
        Assert.Equal(QuestStatus.InProgress, progress.Statuses[47974]);
        Assert.Equal(QuestStatus.NotStarted, progress.Statuses[100]);
        Assert.True(progress.Completed.SetEquals([3692, 3600]));
        Assert.True(progress.Available.SetEquals([47974, 3916]));
        Assert.Equal([3916, 3931], progress.Next);
    }

    [Fact]
    public void Anonymous_page_is_not_linked()
    {
        var result = TrackerPageParser.Parse("""<script>window.PAPYCHA_QUEST_TRACKER = {"nonce":"n","space":null,"views":[]};</script>""");

        Assert.Equal(TrackerReadOutcome.NotLinked, result.Outcome);
        Assert.Null(result.State);
    }

    [Theory]
    [InlineData("<html><body>rien</body></html>")]
    [InlineData(null)]
    public void Page_without_tracker_is_unreadable(string? html)
    {
        Assert.Equal(TrackerReadOutcome.Unreadable, TrackerPageParser.Parse(html).Outcome);
    }

    [Fact]
    public void Broken_json_is_unreadable()
    {
        Assert.Equal(TrackerReadOutcome.Unreadable, TrackerPageParser.Parse("""window.PAPYCHA_QUEST_TRACKER = {"nonce":""").Outcome);
    }

    [Fact]
    public void Empty_statuses_array_is_empty_map()
    {
        var result = TrackerPageParser.Parse("""window.PAPYCHA_QUEST_TRACKER = {"nonce":"n","space":{"characters":[{"id":"a","name":"A","level":1,"quest_statuses":[]}]},"views":[]};""");

        Assert.Empty(result.State!.Progress["a"].Statuses);
    }

    [Theory]
    [InlineData(QuestStatus.NotStarted)]
    [InlineData(QuestStatus.InProgress)]
    [InlineData(QuestStatus.Completed)]
    [InlineData(QuestStatus.Blocked)]
    [InlineData(QuestStatus.Ignored)]
    public void QuestStatusCodes_round_trip(QuestStatus status)
    {
        Assert.Equal(status, QuestStatusCodes.FromSite(QuestStatusCodes.ToSite(status)));
        Assert.Equal(string.Empty, QuestStatusCodes.ToSite(QuestStatus.NotStarted));
        Assert.Equal("in_progress", QuestStatusCodes.ToSite(QuestStatus.InProgress));
    }
}

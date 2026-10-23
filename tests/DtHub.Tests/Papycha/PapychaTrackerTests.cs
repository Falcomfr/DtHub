using System.Net;

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Papycha;

public class PapychaTrackerTests
{
    private const string Linked = """<script>window.PAPYCHA_QUEST_TRACKER = {"nonce":"26794092b6","space":{"active_character_id":"b","characters":[{"id":"b","name":"Falcom","level":190,"quest_statuses":[]}]},"views":{}};</script>""";

    private const string Anonymous = """<script>window.PAPYCHA_QUEST_TRACKER = {"nonce":"n","space":null,"views":[]};</script>""";

    // Answer body that makes the stub throw a network failure.
    private const string Throw = "<throw>";

    private sealed record Seen(HttpMethod Method, string Uri, string? Cookie, string Body);

    private sealed class StubHandler(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _answers = new(answers);

        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("Cookie", out var cookie) ? string.Join(";", cookie) : null,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            var (status, body) = _answers.Dequeue();
            if (body == Throw)
            {
                throw new HttpRequestException("down");
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (PapychaTracker Tracker, StubHandler Handler) Make(params string[] bodies) =>
        Make(new StubHandler([.. bodies.Select(b => (HttpStatusCode.OK, b))]));

    private static (PapychaTracker Tracker, StubHandler Handler) Make(StubHandler handler) =>
        (new PapychaTracker(new HttpClient(handler), NullLogger<PapychaTracker>.Instance), handler);

    [Fact]
    public void StatusForm_has_the_site_fields()
    {
        Assert.Equal(
            [
                new("action", "papycha_tracker_quest_status"),
                new("nonce", "n1"),
                new("character_id", "b"),
                new("quest_id", "3692"),
                new("status", "completed"),
            ],
            PapychaTracker.StatusForm("n1", "b", 3692, QuestStatus.Completed));
    }

    [Fact]
    public async Task Read_sends_the_cookie()
    {
        var (tracker, handler) = Make(Linked);

        var result = await tracker.ReadAsync("abc");

        Assert.Equal(TrackerReadOutcome.Linked, result.Outcome);
        var seen = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, seen.Method);
        Assert.Equal("https://papycha.fr/suivi-quetes/", seen.Uri);
        Assert.Equal("papycha_tracker_code_v2=abc", seen.Cookie);
    }

    [Fact]
    public async Task Read_network_failure_is_unreadable()
    {
        var (tracker, _) = Make(Throw);

        Assert.Equal(TrackerReadOutcome.Unreadable, (await tracker.ReadAsync("abc")).Outcome);
    }

    [Fact]
    public async Task Write_reads_first_then_posts()
    {
        var (tracker, handler) = Make(Linked, """{"success":true,"data":{}}""");

        var result = await tracker.SetStatusAsync("abc", "b", 3692, QuestStatus.Completed);

        Assert.True(result.Success);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("https://papycha.fr/wp-admin/admin-ajax.php", handler.Requests[1].Uri);
        Assert.Contains("nonce=26794092b6", handler.Requests[1].Body);
    }

    [Fact]
    public async Task Write_refused_retries_once_after_a_fresh_read()
    {
        var (tracker, handler) = Make(Linked, """{"success":false,"data":{"message":"Nonce"}}""", Linked, """{"success":true}""");

        var result = await tracker.SetStatusAsync("abc", "b", 3692, QuestStatus.Completed);

        Assert.True(result.Success);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Write_after_phrase_renewed_reports_not_linked()
    {
        var (tracker, handler) = Make(Linked, """{"success":false,"data":{"message":"Accès"}}""", Anonymous);

        var result = await tracker.SetStatusAsync("abc", "b", 3692, QuestStatus.Completed);

        Assert.False(result.Success);
        Assert.Equal(TrackerReadOutcome.NotLinked, result.After);
        Assert.Equal(3, handler.Requests.Count);
    }

    private const string Refused = """{"success":false,"data":{"message":"Nonce"}}""";

    private static readonly TrackerCharacter Falcom = new("b", "Falcom", 191, "female", "enutrof", "#123456");

    [Fact]
    public async Task Save_character_posts_every_field_with_the_nonce()
    {
        var (tracker, handler) = Make(Linked, """{"success":true}""");

        var result = await tracker.SaveCharacterAsync("abc", Falcom);

        Assert.True(result.Success);
        Assert.Equal(
            await new FormUrlEncodedContent(PapychaTracker.CharacterForm("26794092b6", Falcom)).ReadAsStringAsync(),
            handler.Requests[1].Body);
        Assert.Contains("action=papycha_tracker_character&nonce=26794092b6&mode=save&character_id=b&name=Falcom&gender=female&level=191&class_key=enutrof&color=%23123456", handler.Requests[1].Body);
    }

    [Fact]
    public async Task Save_character_refused_retries_once_after_a_fresh_read()
    {
        var (tracker, handler) = Make(Linked, Refused, Linked, """{"success":true}""");

        var result = await tracker.SaveCharacterAsync("abc", Falcom);

        Assert.True(result.Success);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Retry_network_failure_keeps_unreadable()
    {
        var (tracker, _) = Make(Linked, Refused, Linked, Throw);

        var result = await tracker.SetStatusAsync("abc", "b", 1, QuestStatus.Completed);

        Assert.False(result.Success);
        Assert.Equal(TrackerReadOutcome.Unreadable, result.After);
    }

    [Fact]
    public async Task Retry_refused_again_stops_after_four_requests()
    {
        var (tracker, handler) = Make(Linked, Refused, Linked, Refused);

        var result = await tracker.SetStatusAsync("abc", "b", 1, QuestStatus.Completed);

        Assert.False(result.Success);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task First_post_network_failure_does_not_retry()
    {
        var (tracker, handler) = Make(Linked, Throw);

        var result = await tracker.SetStatusAsync("abc", "b", 1, QuestStatus.Completed);

        Assert.False(result.Success);
        Assert.Equal(TrackerReadOutcome.Unreadable, result.After);
        Assert.Equal(2, handler.Requests.Count);

        // Logged, not shown: the bar says "unreadable" in the player's words.
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task Non_json_post_body_is_a_refusal_and_retried()
    {
        var (tracker, handler) = Make(Linked, "<html>403</html>", Linked, """{"success":true}""");

        var result = await tracker.SetStatusAsync("abc", "b", 1, QuestStatus.Completed);

        Assert.True(result.Success);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Write_with_an_unlinked_first_read_does_not_post()
    {
        var (tracker, handler) = Make(Anonymous);

        var result = await tracker.SetStatusAsync("abc", "b", 1, QuestStatus.Completed);

        Assert.False(result.Success);
        Assert.Equal(TrackerReadOutcome.NotLinked, result.After);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retry_posts_the_refreshed_nonce()
    {
        var (tracker, handler) = Make(Linked, Refused, Linked.Replace("26794092b6", "fresh99"), """{"success":true}""");

        await tracker.SetStatusAsync("abc", "b", 1, QuestStatus.Completed);

        Assert.Contains("nonce=fresh99", handler.Requests[3].Body);
    }
}

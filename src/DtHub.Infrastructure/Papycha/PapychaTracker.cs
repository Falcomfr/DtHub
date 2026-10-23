using System.Text.Json;

using DtHub.Core;
using DtHub.Core.Papycha;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Reads and writes the player's quest tracker on papycha.fr.
///
/// The player is identified by a cookie we send as a plain header, so
/// the HttpClient behind this must not keep cookies of its own. A write
/// needs the WordPress nonce of the page, which expires: a refused write
/// is retried once after a fresh read, and no more, so a renewed access
/// phrase ends in "not linked" instead of a loop.
/// </summary>
public sealed partial class PapychaTracker : IPapychaTracker
{
    private readonly HttpClient _http;
    private readonly ILogger<PapychaTracker> _logger;

    // Nonce of the last read; a write without one reads first. Writes are
    // serialised by the caller, so no lock.
    private string? _nonce;

    public PapychaTracker(HttpClient http, ILogger<PapychaTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        _logger = logger;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"{ProductInfo.Slug}/{ProductInfo.Version} (+{ProductInfo.RepositoryUrl})");
        }
    }

    /// <summary>Fields the site's AJAX action expects, in its order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> StatusForm(
        string nonce, string characterId, int questId, QuestStatus status) =>
    [
        new("action", "papycha_tracker_quest_status"),
        new("nonce", nonce),
        new("character_id", characterId),
        new("quest_id", questId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("status", QuestStatusCodes.ToSite(status)),
    ];

    /// <summary>Fields of the site's character save, in its order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> CharacterForm(string nonce, TrackerCharacter c) =>
    [
        new("action", "papycha_tracker_character"),
        new("nonce", nonce),
        new("mode", "save"),
        new("character_id", c.Id),
        new("name", c.Name),
        new("gender", c.Gender),
        new("level", c.Level.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("class_key", c.ClassKey),
        new("color", c.Color),
    ];

    public async Task<TrackerReadResult> ReadAsync(string cookie, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PapychaSite.TrackerUrl);
            request.Headers.TryAddWithoutValidation("Cookie", $"{PapychaSite.TrackerCookie}={cookie}");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = TrackerPageParser.Parse(html);

            // A dead cookie must not keep posting with a stale nonce.
            _nonce = result.State?.Nonce;

            return result;
        }
        catch (Exception ex) when (IsNetworkFailure(ex, cancellationToken))
        {
            LogReadFailed(ex.Message);
            _nonce = null;

            return new TrackerReadResult(TrackerReadOutcome.Unreadable, null);
        }
    }

    public Task<TrackerWriteResult> SetStatusAsync(
        string cookie,
        string characterId,
        int questId,
        QuestStatus status,
        CancellationToken cancellationToken = default) =>
        WriteAsync(cookie, nonce => StatusForm(nonce, characterId, questId, status), cancellationToken);

    public Task<TrackerWriteResult> SaveCharacterAsync(
        string cookie, TrackerCharacter character, CancellationToken cancellationToken = default) =>
        WriteAsync(cookie, nonce => CharacterForm(nonce, character), cancellationToken);

    // Read first if no nonce, post, and on a refusal one fresh read then one retry.
    private async Task<TrackerWriteResult> WriteAsync(
        string cookie, Func<string, IReadOnlyList<KeyValuePair<string, string>>> form, CancellationToken cancellationToken)
    {
        if (_nonce is null)
        {
            var first = await ReadAsync(cookie, cancellationToken).ConfigureAwait(false);
            if (first.Outcome != TrackerReadOutcome.Linked)
            {
                return new TrackerWriteResult(false, null, first.Outcome);
            }
        }

        var attempt = await PostAsync(cookie, form, cancellationToken).ConfigureAwait(false);
        if (attempt.Success || attempt.After is not null)
        {
            return attempt;
        }

        // Refused: most likely a stale nonce, or a renewed phrase. One fresh read tells which.
        var again = await ReadAsync(cookie, cancellationToken).ConfigureAwait(false);
        if (again.Outcome != TrackerReadOutcome.Linked)
        {
            return new TrackerWriteResult(false, attempt.Message, again.Outcome);
        }

        var retry = await PostAsync(cookie, form, cancellationToken).ConfigureAwait(false);

        return retry.After is null ? retry with { After = TrackerReadOutcome.Linked } : retry;
    }

    // After is set here only for a network failure (Unreadable), which a retry would not fix.
    private async Task<TrackerWriteResult> PostAsync(
        string cookie, Func<string, IReadOnlyList<KeyValuePair<string, string>>> form, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, PapychaSite.AjaxUrl)
            {
                Content = new FormUrlEncodedContent(form(_nonce ?? string.Empty)),
            };
            request.Headers.TryAddWithoutValidation("Cookie", $"{PapychaSite.TrackerCookie}={cookie}");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // WordPress answers a bad nonce with 403 and a non-JSON body: that is a refusal too.
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var success = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("success", out var flag)
                    && flag.ValueKind == JsonValueKind.True;

                return new TrackerWriteResult(success, success ? null : MessageOf(root));
            }
            catch (JsonException)
            {
                // Not JSON: the site refused without saying why.
                return new TrackerWriteResult(false, null);
            }
        }
        catch (Exception ex) when (IsNetworkFailure(ex, cancellationToken))
        {
            LogWriteFailed(ex.Message);

            // No message: the reason is logged, and the bar says "unreadable" in the player's words.
            return new TrackerWriteResult(false, null, TrackerReadOutcome.Unreadable);
        }
    }

    private static string? MessageOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("message", out var message)
        && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : null;

    // A timeout cancels the request without the caller's token being cancelled; the caller's own cancel must still propagate.
    private static bool IsNetworkFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    [LoggerMessage(Level = LogLevel.Information, Message = "Suivi Papycha : la page n'a pas pu être lue ({reason}).")]
    private partial void LogReadFailed(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Suivi Papycha : l'état de la quête n'a pas pu être écrit ({reason}).")]
    private partial void LogWriteFailed(string reason);
}

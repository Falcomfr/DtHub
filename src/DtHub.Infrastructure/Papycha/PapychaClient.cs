using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core;
using DtHub.Core.Papycha;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Reads the quest catalog on papycha.fr, through its public content API.
///
/// The site closed its WordPress API on 2026-10-12 and opened this one in
/// its place, for reading and without authentication. Its usage policy
/// (papycha.fr/utilisation-contenus-api/) asks for attribution, which the
/// guides window gives by name and by always opening the site's own page.
/// We identify ourselves: the user agent header names DT Hub, its version
/// and its repository, so that an administrator reading their logs knows
/// who is passing by and can reach us.
///
/// Eight requests for the quests, one for the dungeons, one for the paths,
/// where the closed API needed about fifty.
/// </summary>
public sealed partial class PapychaClient : IPapychaClient
{
    /// <summary>Maximum the API accepts on one page.</summary>
    private const int PageSize = 100;

    /// <summary>
    /// Safety net: the API announces eight pages, we refuse to loop
    /// forever.
    /// </summary>
    private const int MaxPages = 30;

    private static readonly Uri BaseAddress = new("https://papycha.fr/wp-json/papycha-public/v1/");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<PapychaClient> _logger;

    public PapychaClient(HttpClient http, ILogger<PapychaClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        _logger = logger;

        _http.BaseAddress ??= BaseAddress;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"{ProductInfo.Slug}/{ProductInfo.Version} (+{ProductInfo.RepositoryUrl})");
        }
    }

    public async Task<IReadOnlyList<QuestSummary>> GetQuestsAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync<QuestPayload>(
                "quests",
                (loaded, total) => progress?.Report(
                    new QuestIndexingProgress(loaded, total, QuestIndexingPhase.Quests)),
                cancellationToken)
            .ConfigureAwait(false);

        List<QuestSummary> quests = [.. items.Select(ToQuest)];

        LogIndexed(quests.Count);

        return quests;
    }

    public async Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync<DungeonPayload>("dungeons", null, cancellationToken)
            .ConfigureAwait(false);

        List<DungeonSummary> dungeons = [.. items.Select(ToDungeon)];

        LogDungeons(dungeons.Count);

        return dungeons;
    }

    public async Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync<PathPayload>("paths", null, cancellationToken)
            .ConfigureAwait(false);

        List<PathSummary> paths =
        [
            .. items.Select(item => new PathSummary
            {
                Id = item.Id,
                Title = StripPrefix(Decode(item.Name)),
                Url = item.Url ?? string.Empty,
            }),
        ];

        LogPaths(paths.Count);

        return paths;
    }

    /// <summary>
    /// The date the API announces for its last update. One small request,
    /// which is what allows asking often instead of rereading once a week.
    /// </summary>
    public async Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri("status", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogStampUnavailable($"statut HTTP {(int)response.StatusCode}");

                return null;
            }

            var status = await response.Content
                .ReadFromJsonAsync<StatusPayload>(Json, cancellationToken)
                .ConfigureAwait(false);

            return status?.LastUpdated is { } updated ? new SiteStamp(updated) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogStampUnavailable(exception.Message);

            return null;
        }
    }

    /// <summary>
    /// Every page of a family. A refused page is a failure, thrown to the
    /// caller, which keeps the catalog it has: a list cut short would be
    /// taken for the whole.
    /// </summary>
    private async Task<List<T>> ReadAllAsync<T>(
        string family,
        Action<int, int>? progress,
        CancellationToken cancellationToken)
    {
        List<T> all = [];

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response = await _http
                .GetAsync(new Uri($"{family}?per_page={PageSize}&page={page}", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var body = await response.Content
                .ReadFromJsonAsync<PagePayload<T>>(Json, cancellationToken)
                .ConfigureAwait(false);

            all.AddRange(body?.Items ?? []);
            progress?.Invoke(all.Count, body?.Total ?? 0);

            // A page that does not say how many follow is taken as the last
            // one rather than as an invitation to try the next thirty.
            if (body is null || page >= body.TotalPages)
            {
                break;
            }
        }

        return all;
    }

    private static QuestSummary ToQuest(QuestPayload item)
    {
        var title = Decode(item.Name);

        return new QuestSummary
        {
            Id = item.Id,
            Title = title,
            Url = item.Url ?? string.Empty,
            Level = item.Level ?? 0,
            SearchKey = QuestSearch.Normalize(title),

            // Sections first, zones next: the merge files a new quest under
            // whichever of them the catalog already knows.
            Categories = [.. (item.Section ?? []).Concat(item.Zone ?? []).Select(t => t.Id)],
            StartPosition = item.StartCoordinates?.Trim() ?? string.Empty,
            Recurrence = item.Recurrence switch
            {
                "once" => QuestRecurrence.Once,
                "daily" => QuestRecurrence.Daily,
                "weekly" => QuestRecurrence.Weekly,
                "repeatable" => QuestRecurrence.Repeatable,
                "seasonal" => QuestRecurrence.Seasonal,
                _ => QuestRecurrence.Unknown,
            },

            // The conditions are free text, some written as a list item:
            // "- Alignement bontarien 40 minimum.".
            Prerequisites =
            [
                .. (item.AccessConditions ?? [])
                    .Select(c => Decode(c).TrimStart('-', ' ').Trim())
                    .Concat((item.PreviousQuests ?? []).Select(q => Decode(q.Name)))
                    .Where(text => text.Length > 0),
            ],
        };
    }

    private static DungeonSummary ToDungeon(DungeonPayload item)
    {
        var title = StripPrefix(Decode(item.Name));

        return new DungeonSummary
        {
            Id = item.Id,
            Kind = item.Kind switch
            {
                "raid" => DungeonKind.Raid,
                "den" or "lair" => DungeonKind.Lair,
                _ => DungeonKind.Dungeon,
            },
            Title = title,
            Url = item.Url ?? string.Empty,
            SearchKey = QuestSearch.Normalize(title),
            Level = item.Level ?? 0,
            Position = item.EntranceCoordinates?.Trim() ?? string.Empty,
            Person = Decode(item.ContactNpc),
            Key = Decode(item.Key?.Name),
            SoulStone = SoulStone(item.SoulStoneSize),
        };
    }

    /// <summary>
    /// The soul stone's size, as the list names it.
    ///
    /// **Seen on 2026-10-19**: the API sends the level the stone captures
    /// up to, 200, where it sent its name, "Gigantesque". Read as a string,
    /// the number failed the whole catalog refresh on the first dungeon.
    /// Both are read, in case it goes back.
    /// </summary>
    private static string SoulStone(JsonElement size) => size.ValueKind switch
    {
        JsonValueKind.String => Decode(size.GetString()),
        JsonValueKind.Number when size.TryGetInt32(out var level) => level switch
        {
            50 => "Petite",
            100 => "Moyenne",
            150 => "Grande",
            200 => "Gigantesque",
            _ => string.Empty,
        },
        _ => string.Empty,
    };

    /// <summary>
    /// The API still sends some names with their HTML entities, "Bonta
    /// &amp;amp; Cania": left as is, they would show raw and break search.
    /// </summary>
    private static string Decode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlDecode(value).Trim();

    /// <summary>"[Donjon] Abraknydes" reads "Abraknydes" in a list of dungeons.</summary>
    private static string StripPrefix(string title)
    {
        if (!title.StartsWith('['))
        {
            return title;
        }

        var close = title.IndexOf(']', StringComparison.Ordinal);

        return close < 0 ? title : title[(close + 1)..].Trim();
    }

    private sealed class PagePayload<T>
    {
        public List<T>? Items { get; set; }

        public int Total { get; set; }

        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }
    }

    private sealed class TermPayload
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    private sealed class QuestPayload
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Url { get; set; }

        public int? Level { get; set; }

        public List<TermPayload>? Zone { get; set; }

        public List<TermPayload>? Section { get; set; }

        [JsonPropertyName("start_coordinates")]
        public string? StartCoordinates { get; set; }

        public string? Recurrence { get; set; }

        [JsonPropertyName("access_conditions")]
        public List<string>? AccessConditions { get; set; }

        [JsonPropertyName("previous_quests")]
        public List<TermPayload>? PreviousQuests { get; set; }
    }

    private sealed class DungeonPayload
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Url { get; set; }

        public string? Kind { get; set; }

        public int? Level { get; set; }

        [JsonPropertyName("entrance_coordinates")]
        public string? EntranceCoordinates { get; set; }

        [JsonPropertyName("contact_npc")]
        public string? ContactNpc { get; set; }

        public TermPayload? Key { get; set; }

        [JsonPropertyName("soul_stone_size")]
        public JsonElement SoulStoneSize { get; set; }
    }

    private sealed class PathPayload
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Url { get; set; }
    }

    private sealed class StatusPayload
    {
        [JsonPropertyName("last_updated")]
        public DateTimeOffset? LastUpdated { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue papycha lu : {count} quête(s).")]
    private partial void LogIndexed(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Donjons, raids et tanières lus : {count}.")]
    private partial void LogDungeons(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chemins lus : {count}.")]
    private partial void LogPaths(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "L'API de papycha n'a pas dit si elle avait changé ({reason}).")]
    private partial void LogStampUnavailable(string reason);
}

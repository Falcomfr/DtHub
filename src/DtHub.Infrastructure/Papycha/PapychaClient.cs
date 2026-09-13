using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core;
using DtHub.Core.Papycha;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Reads the quest catalog on papycha.fr, through its WordPress API.
///
/// The site is a WordPress whose API is open for reading and whose
/// robots.txt forbids only the admin area. We stick to what it gives,
/// and we identify ourselves: the user agent header names DT Hub, its
/// version and its repository, so that an administrator reading their
/// logs knows who is passing by and can reach us.
///
/// The body of the articles is never requested. Indexing would cost
/// twenty-two megabytes with it, six hundred fifty kilobytes without,
/// and what it contains is obtained for free by opening the page.
/// </summary>
public sealed partial class PapychaClient : IPapychaClient
{
    /// <summary>Category that holds all the quests on the site.</summary>
    private const int QuestCategory = 7;

    /// <summary>
    /// The categories of combat locations: dungeons, raids, lairs.
    /// Eighty-three, two and eight articles.
    /// </summary>
    private static readonly (int Category, DungeonKind Kind)[] DungeonCategories =
    [
        (6, DungeonKind.Dungeon),
        (741, DungeonKind.Raid),
        (721, DungeonKind.Lair),
    ];

    /// <summary>The "[Chemins]" category, which holds twenty-one.</summary>
    private const int PathCategory = 8;

    /// <summary>Maximum accepted by WordPress on one page.</summary>
    private const int PageSize = 100;

    /// <summary>
    /// Safety net: the site announces eight, we refuse to loop forever.
    /// </summary>
    private const int MaxPages = 30;

    private static readonly Uri BaseAddress = new("https://papycha.fr/wp-json/wp/v2/");

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
        List<QuestSummary> quests = [];
        var total = 0;

        // Before the quests: each one carries only the id of its starting
        // character, and this table is needed to get a name out of it.
        var people = await GetPeopleAsync(cancellationToken).ConfigureAwait(false);

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var address =
                $"posts?categories={QuestCategory}&per_page={PageSize}&page={page}"
                + "&orderby=title&order=asc"
                + "&_fields=id,title,link,categories,papycha_type_quete,meta";

            using var response = await _http.GetAsync(address, cancellationToken).ConfigureAwait(false);

            // WordPress returns 400 when a page beyond the last one is
            // requested: this is the end of the run, not a failure.
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && page > 1)
            {
                break;
            }

            response.EnsureSuccessStatusCode();

            if (total == 0
                && response.Headers.TryGetValues("X-WP-Total", out var values)
                && int.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, out var announced))
            {
                total = announced;
            }

            var items = await response.Content
                .ReadFromJsonAsync<List<PostPayload>>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (items is not { Count: > 0 })
            {
                break;
            }

            quests.AddRange(items.Select(item => ToSummary(item, people)));
            progress?.Report(
                new QuestIndexingProgress(quests.Count, total, QuestIndexingPhase.Quests));

            if (items.Count < PageSize)
            {
                break;
            }
        }

        LogIndexed(quests.Count);

        return quests;
    }

    /// <inheritdoc />
    public Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default) =>
        StampAsync(category: null, cancellationToken);

    private async Task<SiteStamp?> StampAsync(int? category, CancellationToken cancellationToken)
    {
        // A single article, the last one modified, and two fields. The site
        // returns ninety-seven bytes, and its header gives the total count.
        // This is what allows asking often instead of rereading once a
        // week.
        var url = "posts?per_page=1&orderby=modified&order=desc&_fields=modified_gmt";

        if (category is { } only)
        {
            url += "&categories=" + only.ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            using var response = await _http
                .GetAsync(new Uri(BaseAddress, url), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode
                || !response.Headers.TryGetValues("X-WP-Total", out var values)
                || !int.TryParse(
                    values.FirstOrDefault(), CultureInfo.InvariantCulture, out var posts))
            {
                return null;
            }

            var items = await response.Content
                .ReadFromJsonAsync<List<StampPayload>>(Json, cancellationToken)
                .ConfigureAwait(false);

            var modified = items?.FirstOrDefault()?.ModifiedGmt;

            // The site writes its dates without a time zone, in universal
            // time: without saying so, they would be read in the machine's
            // own zone and would appear two hours ahead or behind.
            return modified is null
                ? null
                : new SiteStamp(
                    new DateTimeOffset(
                        DateTime.SpecifyKind(modified.Value, DateTimeKind.Utc)),
                    posts);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            LogStampUnavailable();

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryStamp>> GetCategoryStampsAsync(
        CancellationToken cancellationToken = default)
    {
        List<CategoryStamp> stamps = [];

        foreach (var category in Watched)
        {
            var stamp = await StampAsync(category, cancellationToken).ConfigureAwait(false);

            if (stamp is null)
            {
                // A silent category makes comparison impossible: better to
                // keep nothing than to keep it halfway, which would make
                // things look unchanged.
                return [];
            }

            stamps.Add(new CategoryStamp(category, stamp.Modified, stamp.Posts));
        }

        return stamps;
    }

    /// <summary>
    /// The categories we read something from, and only those.
    /// </summary>
    private static IEnumerable<int> Watched =>
        [QuestCategory, PathCategory, .. DungeonCategories.Select(c => c.Category)];

    public async Task<IReadOnlyList<QuestSection>> GetSectionsAsync(
        CancellationToken cancellationToken = default)
    {
        List<QuestSection> sections = [];

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var address =
                $"categories?per_page={PageSize}&page={page}&orderby=name&order=asc"
                + "&_fields=id,name,parent,count";

            using var response = await _http.GetAsync(address, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && page > 1)
            {
                break;
            }

            response.EnsureSuccessStatusCode();

            var items = await response.Content
                .ReadFromJsonAsync<List<TermPayload>>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (items is not { Count: > 0 })
            {
                break;
            }

            sections.AddRange(items
                .Where(t => t.Count > 0)
                .Select(t => new QuestSection
                {
                    Id = t.Id,
                    Name = Decode(t.Name),
                    Parent = t.Parent,
                    Count = t.Count,
                    SearchKey = QuestSearch.Normalize(Decode(t.Name)),
                }));

            if (items.Count < PageSize)
            {
                break;
            }
        }

        return sections;
    }

    /// <summary>
    /// The listing kept by hand on the "Quêtes" page, and the content of
    /// each of the pages it enumerates.
    ///
    /// About twenty requests, once a week, for content alone: a section
    /// page weighs a dozen kilobytes through the API against three hundred
    /// in full HTML. A page that does not respond is skipped, it does not
    /// make the others fail.
    /// </summary>
    public async Task<IReadOnlyList<QuestPageSection>> GetPageSectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var index = await GetPageContentAsync("quetes", cancellationToken).ConfigureAwait(false);

        if (index is null)
        {
            return [];
        }

        var listed = QuestSectionPageParser.ParseIndex(index);

        if (listed.Count == 0)
        {
            LogSectionsUnavailable("le tableau de la page « Quêtes » est illisible");

            return [];
        }

        // **Four at a time, and not one more.** These reads are almost pure
        // latency, a dozen kilobytes each: chaining them one by one cost
        // twenty-five round trips end to end for three hundred kilobytes in
        // total. This is the only place in the indexing where parallelism
        // truly pays off.
        //
        // The cap is a courtesy choice and not a technical limit: the site
        // is run by one person, and nothing justifies sending them
        // twenty-five simultaneous requests to save one more second.
        using var gate = new SemaphoreSlim(SectionParallelism, SectionParallelism);

        var read = await Task.WhenAll(listed.Select(async section =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var content = await GetPageContentAsync(
                    PageReference(section.Url), cancellationToken).ConfigureAwait(false);

                return content is null
                    ? null
                    : section with
                    {
                        QuestUrls = QuestSectionPageParser.ParseQuestLinks(content),
                        Groups = QuestSectionPageParser.ParseGroups(content),
                    };
            }
            finally
            {
                _ = gate.Release();
            }
        })).ConfigureAwait(false);

        // The order of the "Quêtes" page is kept: "Task.WhenAll" returns
        // the results in the order of the tasks, not that of the
        // responses. This order is the one in which the site arranges its
        // sections, and the window relies on it.
        List<QuestPageSection> sections = [.. read.OfType<QuestPageSection>()];

        LogSectionsRead(sections.Count);

        return sections;
    }

    /// <summary>
    /// Section page reads carried out in parallel. Four, out of courtesy
    /// for a site run by one person.
    /// </summary>
    private const int SectionParallelism = 4;

    /// <summary>
    /// What identifies a page in the API: its id when the address carries
    /// it, its last segment otherwise. The site uses both forms in its own
    /// table.
    /// </summary>
    private static string PageReference(string url)
    {
        var marker = url.IndexOf("page_id=", StringComparison.OrdinalIgnoreCase);

        if (marker >= 0)
        {
            var digits = url[(marker + 8)..];
            var end = digits.IndexOfAny(['&', '#']);

            return end >= 0 ? digits[..end] : digits;
        }

        var segments = url.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 ? segments[^1] : string.Empty;
    }

    /// <summary>
    /// Rendered content of a page, identified by its id or by the last
    /// segment of its address.
    /// </summary>
    private async Task<string?> GetPageContentAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var address = reference.All(char.IsAsciiDigit)
            ? $"pages/{reference}?_fields=content"
            : $"pages?slug={Uri.EscapeDataString(reference)}&_fields=content";

        try
        {
            using var response = await _http
                .GetAsync(new Uri(address, UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // An id returns the page alone, a slug returns an array: the
            // same endpoint answers in two forms depending on the question.
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                {
                    return null;
                }

                root = root[0];
            }

            return root.TryGetProperty("content", out var content)
                   && content.TryGetProperty("rendered", out var rendered)
                ? rendered.GetString()
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            LogSectionsUnavailable(exception.Message);

            return null;
        }
    }

    private static QuestSummary ToSummary(PostPayload post, IReadOnlyDictionary<int, string> people)
    {
        var title = Decode(post.Title?.Rendered);
        var person = post.Meta?.StartPersonId ?? 0;

        return new QuestSummary
        {
            Id = post.Id,
            Title = title,
            Url = post.Link ?? string.Empty,
            Level = post.Meta?.Level ?? 0,
            Categories = post.Categories ?? [],
            Types = post.QuestTypes ?? [],
            SearchKey = QuestSearch.Normalize(title),
            Prerequisites = Lines(post.Meta?.Prerequisites),
            StartPosition = Decode(post.Meta?.StartPosition).Trim(),
            StartPerson = person > 0 ? people.GetValueOrDefault(person, string.Empty) : string.Empty,
        };
    }

    /// <summary>
    /// Splits a prerequisite into lines. The site sometimes puts two in
    /// the same field, separated by a line break: showing them stuck
    /// together would make them unreadable.
    /// </summary>
    private static IReadOnlyList<string> Lines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            :
            [
                .. Decode(value)
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ];

    /// <summary>
    /// Character names, by id.
    ///
    /// The site stores a quest's starting character as an id: without
    /// this table, we would know there is one without knowing which. Three
    /// hundred seventy-three names, four requests, once per indexing run.
    ///
    /// Returns an empty table if the site does not respond: the quest then
    /// keeps its starting position, and only loses the name.
    /// </summary>
    private async Task<Dictionary<int, string>> GetPeopleAsync(CancellationToken cancellationToken)
    {
        Dictionary<int, string> people = [];

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var address = $"papycha_pnj?per_page={PageSize}&page={page}&_fields=id,name";

            try
            {
                using var response = await _http
                    .GetAsync(new Uri(address, UriKind.Relative), cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    break;
                }

                var terms = await response.Content
                    .ReadFromJsonAsync<List<TermPayload>>(Json, cancellationToken)
                    .ConfigureAwait(false);

                if (terms is not { Count: > 0 })
                {
                    break;
                }

                foreach (var term in terms)
                {
                    people[term.Id] = Decode(term.Name);
                }

                if (terms.Count < PageSize)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                LogPeopleUnavailable(exception.Message);

                break;
            }
        }

        return people;
    }

    /// <summary>
    /// WordPress returns titles with entities: "L&#8217;Automne". Leaving
    /// them as is would show them raw and would break search.
    /// </summary>
    private static string Decode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlDecode(value);

    /// <summary>
    /// The dungeons, content included, in one request.
    ///
    /// The level, the position and the character come from the metadata;
    /// the key and the soul stone live only in the article body, hence
    /// requesting the rendered content. Four megabytes for eighty-three
    /// dungeons, once per indexing run: the price of one request instead
    /// of eighty-three.
    /// </summary>
    public async Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default)
    {
        List<DungeonSummary> dungeons = [];

        foreach (var (category, kind) in DungeonCategories)
        {
            foreach (var item in await ReadCategoryAsync(category, cancellationToken).ConfigureAwait(false))
            {
                dungeons.Add(ToDungeon(item, kind));
            }
        }

        LogDungeons(dungeons.Count);

        return dungeons;
    }

    public async Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        IReadOnlyList<string> dungeonTitles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dungeonTitles);

        List<PathSummary> paths = [];

        foreach (var item in await ReadCategoryAsync(PathCategory, cancellationToken).ConfigureAwait(false))
        {
            var title = StripPrefix(Decode(item.Title?.Rendered));

            paths.Add(new PathSummary
            {
                Id = item.Id,
                Title = title,
                Url = item.Link ?? string.Empty,
                SearchKey = QuestSearch.Normalize(title),
                Side = PathTarget.Of(title, dungeonTitles),
            });
        }

        LogPaths(paths.Count, paths.Count(p => p.Side == PathSide.Dungeons));

        return paths;
    }

    /// <summary>
    /// The articles of a category, rendered content included, page after
    /// page.
    ///
    /// The content comes with the rest and not article by article: the
    /// key, the soul stone and the raids' level live only in the body, and
    /// requesting them separately would cost one hundred fourteen requests
    /// where three suffice.
    /// </summary>
    private async Task<List<DungeonPayload>> ReadCategoryAsync(
        int category,
        CancellationToken cancellationToken)
    {
        List<DungeonPayload> all = [];

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var address =
                $"posts?categories={category}&per_page={PageSize}&page={page}"
                + "&orderby=title&order=asc"
                + "&_fields=id,title,link,meta,content";

            using var response = await _http.GetAsync(address, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && page > 1)
            {
                break;
            }

            response.EnsureSuccessStatusCode();

            var items = await response.Content
                .ReadFromJsonAsync<List<DungeonPayload>>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (items is not { Count: > 0 })
            {
                break;
            }

            all.AddRange(items);

            if (items.Count < PageSize)
            {
                break;
            }
        }

        return all;
    }

    private static DungeonSummary ToDungeon(DungeonPayload item, DungeonKind kind)
    {
        var title = StripPrefix(Decode(item.Title?.Rendered));
        var html = item.Content?.Rendered ?? string.Empty;

        return new DungeonSummary
        {
            Id = item.Id,
            Kind = kind,
            Title = title,
            Url = item.Link ?? string.Empty,
            SearchKey = QuestSearch.Normalize(title),

            // The metadata first, the prose next: dungeons put their level
            // there, raids and lairs write it in plain text.
            Level = item.Meta?.DungeonLevel is > 0 and var level
                ? level
                : DungeonPageParser.ParseLevel(html),
            Position = (item.Meta?.DungeonPosition ?? string.Empty).Trim(),
            Person = Decode(item.Meta?.DungeonPerson).Trim(),
            Key = DungeonPageParser.ParseKey(html),
            SoulStone = DungeonPageParser.ParseSoulStone(html),
        };
    }

    /// <summary>
    /// The name without the bracketed prefix the site puts on all its
    /// titles. It reads well on a page, poorly in a list where every line
    /// is of the same kind.
    /// </summary>
    private static string StripPrefix(string title)
    {
        var value = title.Trim();

        if (!value.StartsWith('['))
        {
            return value;
        }

        var close = value.IndexOf(']', StringComparison.Ordinal);

        return close < 0 ? value : value[(close + 1)..].Trim();
    }

    private sealed class DungeonPayload
    {
        public int Id { get; set; }

        public RenderedText? Title { get; set; }

        public string? Link { get; set; }

        public RenderedText? Content { get; set; }

        public MetaPayload? Meta { get; set; }
    }

    /// <summary>The only field the sentinel asks for.</summary>
    private sealed class StampPayload
    {
        [JsonPropertyName("modified_gmt")]
        public DateTime? ModifiedGmt { get; set; }
    }

    private sealed class PostPayload
    {
        public int Id { get; set; }

        public RenderedText? Title { get; set; }

        public string? Link { get; set; }

        public List<int>? Categories { get; set; }

        [JsonPropertyName("papycha_type_quete")]
        public List<int>? QuestTypes { get; set; }

        public MetaPayload? Meta { get; set; }
    }

    private sealed class RenderedText
    {
        public string? Rendered { get; set; }
    }

    private sealed class MetaPayload
    {
        [JsonPropertyName("_pqa_level")]
        public int Level { get; set; }

        [JsonPropertyName("_pqa_start_position")]
        public string? StartPosition { get; set; }

        [JsonPropertyName("_pqa_start_person_id")]
        public int StartPersonId { get; set; }

        [JsonPropertyName("_pqa_prerequisites")]
        public string? Prerequisites { get; set; }

        // Dungeons have their own metadata prefix. They share this type
        // rather than having a second one: absent fields stay at their
        // default value, and the API returns them all anyway.
        [JsonPropertyName("_pcd_level")]
        public int DungeonLevel { get; set; }

        [JsonPropertyName("_pcd_position")]
        public string? DungeonPosition { get; set; }

        [JsonPropertyName("_pcd_npc")]
        public string? DungeonPerson { get; set; }
    }

    private sealed class TermPayload
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public int Parent { get; set; }

        public int Count { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue papycha indexé : {count} quête(s).")]
    private partial void LogIndexed(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Donjons, raids et tanières indexés : {count}.")]
    private partial void LogDungeons(int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Chemins indexés : {count}, dont {dungeons} côté donjons.")]
    private partial void LogPaths(int count, int dungeons);


    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Les personnages n'ont pas pu être lus ({reason}) ; les quêtes garderont leur position sans le nom.")]
    private partial void LogPeopleUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le site n'a pas dit s'il avait changé.")]
    private partial void LogStampUnavailable();

    [LoggerMessage(Level = LogLevel.Information, Message = "Rubriques du site lues : {count}.")]
    private partial void LogSectionsRead(int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Une rubrique du site n'a pas pu être lue ({reason}) ; les catégories suffisent à ranger le reste.")]
    private partial void LogSectionsUnavailable(string reason);
}

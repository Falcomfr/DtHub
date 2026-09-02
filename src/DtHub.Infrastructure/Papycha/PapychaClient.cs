using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core;
using DtHub.Core.Papycha;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Lit le catalogue des quêtes sur papycha.fr, par son API WordPress.
///
/// Le site est un WordPress dont l'API est ouverte en lecture et dont le
/// robots.txt n'interdit que l'administration. On s'en tient à ce qu'elle
/// donne, et on s'annonce : l'en-tête d'agent nomme DT Hub, sa version et son
/// dépôt, pour qu'un administrateur qui lit ses journaux sache qui passe et
/// puisse nous joindre.
///
/// Le corps des articles n'est jamais demandé. L'indexation coûterait vingt-
/// deux mégaoctets avec, six cent cinquante kilooctets sans, et ce qu'il
/// contient s'obtient gratuitement en ouvrant la page.
/// </summary>
public sealed partial class PapychaClient : IPapychaClient
{
    /// <summary>Catégorie qui range toutes les quêtes du site.</summary>
    private const int QuestCategory = 7;

    /// <summary>
    /// Les catégories des lieux de combat : donjons, raids, tanières.
    /// Quatre-vingt-trois, deux et huit articles.
    /// </summary>
    private static readonly (int Category, DungeonKind Kind)[] DungeonCategories =
    [
        (6, DungeonKind.Dungeon),
        (741, DungeonKind.Raid),
        (721, DungeonKind.Lair),
    ];

    /// <summary>La catégorie « [Chemins] », qui en range vingt et un.</summary>
    private const int PathCategory = 8;

    /// <summary>Maximum accepté par WordPress sur une page.</summary>
    private const int PageSize = 100;

    /// <summary>Garde-fou : le site en annonce huit, on refuse de boucler sans fin.</summary>
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

        // Avant les quêtes : chacune ne porte que l'identifiant de son
        // personnage de départ, et il faut cette table pour en tirer un nom.
        var people = await GetPeopleAsync(cancellationToken).ConfigureAwait(false);

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var address =
                $"posts?categories={QuestCategory}&per_page={PageSize}&page={page}"
                + "&orderby=title&order=asc"
                + "&_fields=id,title,link,categories,papycha_type_quete,meta";

            using var response = await _http.GetAsync(address, cancellationToken).ConfigureAwait(false);

            // WordPress rend 400 quand on demande une page au-delà de la
            // dernière : c'est la fin du parcours, pas une panne.
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
            progress?.Report(new QuestIndexingProgress(quests.Count, total));

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
        // Un seul article, le dernier modifié, et deux champs. Le site rend
        // quatre-vingt-dix-sept octets, et son en-tête donne le compte total.
        // C'est ce qui permet de demander souvent au lieu de relire une fois
        // par semaine.
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

            // Le site écrit ses dates sans fuseau, en temps universel : sans le
            // dire, elles seraient lues dans celui de la machine et paraîtraient
            // en avance ou en retard de deux heures.
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
                // Une catégorie muette rend la comparaison impossible : mieux
                // vaut ne rien retenir que retenir à moitié, ce qui ferait
                // croire au repos.
                return [];
            }

            stamps.Add(new CategoryStamp(category, stamp.Modified, stamp.Posts));
        }

        return stamps;
    }

    /// <summary>Les catégories dont on lit quelque chose, et elles seules.</summary>
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
    /// Le classement tenu à la main sur la page « Quêtes », et le contenu de
    /// chacune des pages qu'il énumère.
    ///
    /// Une vingtaine de requêtes, une fois par semaine, sur le contenu seul :
    /// une page de rubrique pèse une douzaine de kilooctets par l'API contre
    /// trois cents en HTML complet. Une page qui ne répond pas est passée, elle
    /// ne fait pas échouer les autres.
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

        List<QuestPageSection> sections = [];

        foreach (var section in listed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await GetPageContentAsync(
                PageReference(section.Url), cancellationToken).ConfigureAwait(false);

            if (content is null)
            {
                continue;
            }

            sections.Add(section with
            {
                QuestUrls = QuestSectionPageParser.ParseQuestLinks(content),
                Groups = QuestSectionPageParser.ParseGroups(content),
            });
        }

        LogSectionsRead(sections.Count);

        return sections;
    }

    /// <summary>
    /// Ce qui identifie une page dans l'API : son identifiant quand l'adresse
    /// le porte, son dernier segment sinon. Le site emploie les deux formes
    /// dans son propre tableau.
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
    /// Contenu rendu d'une page, désignée par son identifiant ou par son
    /// dernier segment d'adresse.
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

            // Un identifiant rend la page seule, un slug rend un tableau : le
            // même point d'entrée répond dans deux formes selon la question.
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
    /// Découpe un prérequis en lignes. Le site en met parfois deux dans le même
    /// champ, séparés par un retour : les afficher collés les rendrait illisibles.
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
    /// Noms des personnages, par identifiant.
    ///
    /// Le site range le personnage de départ d'une quête sous forme
    /// d'identifiant : sans cette table, on saurait qu'il y en a un sans savoir
    /// lequel. Trois cent soixante-treize noms, quatre requêtes, une fois par
    /// indexation.
    ///
    /// Rend une table vide si le site ne répond pas : la quête garde alors sa
    /// position de départ, et perd seulement le nom.
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
    /// WordPress rend les titres avec des entités : « L&#8217;Automne ». Les
    /// laisser telles quelles les afficherait crues et casserait la recherche.
    /// </summary>
    private static string Decode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlDecode(value);

    /// <summary>
    /// Les donjons, contenu compris, en une requête.
    ///
    /// Le niveau, la position et le personnage viennent des métadonnées ; la
    /// clef et la pierre d'âme ne vivent que dans le corps de l'article, d'où
    /// la demande du contenu rendu. Quatre mégaoctets pour quatre-vingt-trois
    /// donjons, une fois par indexation : le prix d'une requête au lieu de
    /// quatre-vingt-trois.
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
    /// Les articles d'une catégorie, contenu rendu compris, page après page.
    ///
    /// Le contenu vient avec le reste et non article par article : la clef, la
    /// pierre d'âme et le niveau des raids ne vivent que dans le corps, et les
    /// demander séparément coûterait cent quatorze requêtes là où trois
    /// suffisent.
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

            // Les métadonnées d'abord, la prose ensuite : les donjons y mettent
            // leur niveau, les raids et les tanières l'écrivent en clair.
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
    /// Le nom sans le préfixe entre crochets que le site met à tous ses titres.
    /// Il se lit bien dans une page, mal dans une liste où toutes les lignes
    /// sont de la même sorte.
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

    /// <summary>Le seul champ que la sentinelle demande.</summary>
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

        // Les donjons ont leur propre préfixe de métadonnées. Elles partagent
        // ce type plutôt que d'en avoir un second : les champs absents restent
        // à leur valeur par défaut, et l'API les rend tous de toute façon.
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

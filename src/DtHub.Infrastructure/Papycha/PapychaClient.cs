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

            quests.AddRange(items.Select(ToSummary));
            progress?.Report(new QuestIndexingProgress(quests.Count, total));

            if (items.Count < PageSize)
            {
                break;
            }
        }

        LogIndexed(quests.Count);

        return quests;
    }

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

    private static QuestSummary ToSummary(PostPayload post)
    {
        var title = Decode(post.Title?.Rendered);

        return new QuestSummary
        {
            Id = post.Id,
            Title = title,
            Url = post.Link ?? string.Empty,
            Level = post.Meta?.Level ?? 0,
            Categories = post.Categories ?? [],
            Types = post.QuestTypes ?? [],
            SearchKey = QuestSearch.Normalize(title),
        };
    }

    /// <summary>
    /// WordPress rend les titres avec des entités : « L&#8217;Automne ». Les
    /// laisser telles quelles les afficherait crues et casserait la recherche.
    /// </summary>
    private static string Decode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlDecode(value);

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


    [LoggerMessage(Level = LogLevel.Information, Message = "Rubriques du site lues : {count}.")]
    private partial void LogSectionsRead(int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Une rubrique du site n'a pas pu être lue ({reason}) ; les catégories suffisent à ranger le reste.")]
    private partial void LogSectionsUnavailable(string reason);
}

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
    /// L'ordre du site, lu dans le menu que porte chacune de ses pages. Une
    /// seule page suffit, et on prend celle des quêtes.
    ///
    /// Un échec ici n'est pas grave : sans cet ordre, les rubriques se rangent
    /// par nombre de quêtes. On rend donc une liste vide plutôt que de faire
    /// échouer toute l'indexation pour une question de présentation.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSectionOrderAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(new Uri("https://papycha.fr/quetes/"), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return QuestMenuParser.ParseOrder(html);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            LogOrderUnavailable(exception.Message);

            return [];
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "L'ordre des rubriques n'a pas pu être lu ({reason}) ; classement par nombre de quêtes.")]
    private partial void LogOrderUnavailable(string reason);
}

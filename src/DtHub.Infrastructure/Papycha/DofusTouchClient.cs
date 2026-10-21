using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using DtHub.Core;
using DtHub.Core.Papycha;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Reads the data server of DOFUS Touch, which answers the game's own
/// requests: POST with the data class and the ids, JSON back, keyed by id.
/// Like <see cref="PapychaClient"/>, it names DT Hub in its user agent.
/// A refusal answers an HTML page (nginx), never JSON, so a non-success
/// status throws before any parsing.
/// </summary>
public sealed partial class DofusTouchClient : IDofusTouchClient
{
    private static readonly Uri BaseAddress = new("https://dt-proxy-production-login.ankama-games.com/data/");

    private readonly HttpClient _http;
    private readonly ILogger<DofusTouchClient> _logger;

    public DofusTouchClient(HttpClient http, ILogger<DofusTouchClient> logger)
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

    public async Task<IReadOnlyDictionary<int, JsonElement>> GetAsync(
        string dataClass,
        IReadOnlyCollection<int>? ids,
        string language,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object> { ["class"] = dataClass };

        if (ids is not null)
        {
            body["ids"] = ids;
        }

        return await PostAsync("map", body, language, element => element.Clone(), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyDictionary<int, string>> GetTextsAsync(
        IReadOnlyCollection<int> ids,
        string language,
        CancellationToken cancellationToken = default) =>
        PostAsync("text", new { ids }, language, element => element.GetString() ?? string.Empty, cancellationToken);

    private async Task<IReadOnlyDictionary<int, T>> PostAsync<T>(
        string route,
        object body,
        string language,
        Func<JsonElement, T> convert,
        CancellationToken cancellationToken)
    {
        using var response = await _http
            .PostAsJsonAsync($"{route}?lang={Uri.EscapeDataString(language)}", body, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var rows = new Dictionary<int, T>();

        foreach (var property in document.RootElement.EnumerateObject())
        {
            rows[int.Parse(property.Name, CultureInfo.InvariantCulture)] = convert(property.Value);
        }

        LogRead(route, language, rows.Count);

        return rows;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Données DOFUS Touch {route} ({language}) : {count} ligne(s).")]
    private partial void LogRead(string route, string language, int count);
}

using System.Net;
using System.Net.Http.Headers;

using DtHub.Core.Updates;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Updates;

/// <summary>
/// Les livraisons publiées sur le dépôt.
///
/// Sans jeton : l'API du dépôt rend les livraisons publiques à qui les demande,
/// soixante fois par heure et par adresse, ce qui dépasse de loin une
/// vérification par démarrage. Un jeton posé dans l'exécutable serait de toute
/// façon lisible par qui l'ouvre.
///
/// Rien de ce qui rate ici n'est une panne : pas de réseau, dépôt encore
/// absent, quota atteint, l'application continue sans mise à jour. C'est un
/// service de confort, pas une dépendance.
/// </summary>
public sealed partial class GitHubReleaseSource(
    HttpClient client,
    string owner,
    string repository,
    ILogger<GitHubReleaseSource> logger) : IReleaseSource
{
    private readonly HttpClient _client = client;
    private readonly string _owner = owner;
    private readonly string _repository = repository;
    private readonly ILogger<GitHubReleaseSource> _logger = logger;

    /// <inheritdoc />
    public async Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repository}/releases/latest";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // L'API refuse une demande anonyme : elle veut savoir qui parle.
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DtHub", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Ni dépôt ni livraison : l'application est simplement seule au
                // monde, ce qui est le cas tant que rien n'est publié.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogRefused((int)response.StatusCode);

                return null;
            }

            var json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return ReleaseParser.Parse(json, UpdatePaths.Executable);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(exception);

            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut();

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string> ReadAsync(string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DtHub", "1.0"));

        using var response = await _client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        _ = response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(
        string url,
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DtHub", "1.0"));

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        _ = response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var target = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long done = 0;

        while (true)
        {
            var read = await source
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            await target
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);

            done += read;

            if (total > 0)
            {
                progress?.Report((double)done / total);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le dépôt a refusé la demande de livraison : {code}.")]
    private partial void LogRefused(int code);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le dépôt n'a pas répondu.")]
    private partial void LogUnreachable(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le dépôt a mis trop de temps à répondre.")]
    private partial void LogTimedOut();
}

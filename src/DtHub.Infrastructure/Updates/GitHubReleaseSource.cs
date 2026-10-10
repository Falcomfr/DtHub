using System.Net;
using System.Net.Http.Headers;

using DtHub.Core.Updates;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Updates;

/// <summary>
/// Releases published on the repository.
///
/// No API: "releases/latest" on the site redirects to the page of the
/// last release, and its assets sit at fixed addresses beside it. See
/// <see cref="ReleaseParser.FromTagPage" /> for why the API is avoided.
///
/// Nothing that fails here is an outage: no network, repository not
/// yet there, quota reached, the application keeps running without
/// an update. This is a convenience service, not a dependency.
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
        var url = $"https://github.com/{_owner}/{_repository}/releases/latest";

        try
        {
            // HEAD: only the address the redirect lands on is read, not
            // the page itself.
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DtHub", "1.0"));

            using var response = await _client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No repository: the application is simply alone in
                // the world.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogRefused((int)response.StatusCode);

                return null;
            }

            return response.RequestMessage?.RequestUri is { } landed
                ? ReleaseParser.FromTagPage(landed, UpdatePaths.Executable)
                : null;
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

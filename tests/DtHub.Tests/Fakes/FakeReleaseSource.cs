using DtHub.Core.Updates;

namespace DtHub.Tests.Fakes;

/// <summary>A release source that never leaves memory.</summary>
public sealed class FakeReleaseSource : IReleaseSource
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> _failing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hanging = new(StringComparer.Ordinal);

    public AppRelease? Latest { get; set; }

    public int Downloads { get; private set; }

    public FakeReleaseSource WithText(string url, string content)
    {
        _texts[url] = content;

        return this;
    }

    /// <summary>
    /// Reading this address fails, by default as a network that drops
    /// would.
    /// </summary>
    public FakeReleaseSource WithFailure(string url, Exception? error = null)
    {
        _failing[url] = error ?? new HttpRequestException("Pas de réseau.");

        return this;
    }

    /// <summary>Reading this address never answers until it is cancelled.</summary>
    public FakeReleaseSource WithHang(string url)
    {
        _ = _hanging.Add(url);

        return this;
    }

    public FakeReleaseSource WithFile(string url, byte[] content)
    {
        _files[url] = content;

        return this;
    }

    public Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Latest);

    public async Task<string> ReadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (_hanging.Contains(url))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        return _failing.TryGetValue(url, out var error)
            ? throw error
            : _texts.GetValueOrDefault(url, string.Empty);
    }

    public async Task DownloadAsync(
        string url,
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Downloads++;

        await File
            .WriteAllBytesAsync(path, _files.GetValueOrDefault(url, []), cancellationToken)
            .ConfigureAwait(false);
    }
}

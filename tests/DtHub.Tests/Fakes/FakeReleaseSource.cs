using DtHub.Core.Updates;

namespace DtHub.Tests.Fakes;

/// <summary>A release source that never leaves memory.</summary>
public sealed class FakeReleaseSource : IReleaseSource
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failing = new(StringComparer.Ordinal);

    public AppRelease? Latest { get; set; }

    public int Downloads { get; private set; }

    public FakeReleaseSource WithText(string url, string content)
    {
        _texts[url] = content;

        return this;
    }

    /// <summary>Reading this address fails, as a network that drops would.</summary>
    public FakeReleaseSource WithFailure(string url)
    {
        _ = _failing.Add(url);

        return this;
    }

    public FakeReleaseSource WithFile(string url, byte[] content)
    {
        _files[url] = content;

        return this;
    }

    public Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Latest);

    public Task<string> ReadAsync(string url, CancellationToken cancellationToken = default) =>
        _failing.Contains(url)
            ? Task.FromException<string>(new HttpRequestException("Pas de réseau."))
            : Task.FromResult(_texts.GetValueOrDefault(url, string.Empty));

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

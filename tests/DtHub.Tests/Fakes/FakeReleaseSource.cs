using DtHub.Core.Updates;

namespace DtHub.Tests.Fakes;

/// <summary>Une source de livraisons qui ne sort pas de la mémoire.</summary>
public sealed class FakeReleaseSource : IReleaseSource
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public AppRelease? Latest { get; set; }

    public int Downloads { get; private set; }

    public FakeReleaseSource WithText(string url, string content)
    {
        _texts[url] = content;

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
        Task.FromResult(_texts.GetValueOrDefault(url, string.Empty));

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

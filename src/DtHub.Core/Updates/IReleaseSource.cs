namespace DtHub.Core.Updates;

/// <summary>
/// Where releases come from. An interface so that the decision to
/// update can be tested without a network.
/// </summary>
public interface IReleaseSource
{
    /// <summary>
    /// The latest published release, or <c>null</c> if there is none,
    /// if the repository does not exist yet, or if the network does
    /// not respond. An update check that fails is not a breakdown:
    /// the application carries on.
    /// </summary>
    Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The content of a small file from the release, the digest.
    /// </summary>
    Task<string> ReadAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the executable to a file, reporting its progress
    /// from zero to one.
    /// </summary>
    Task DownloadAsync(
        string url,
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

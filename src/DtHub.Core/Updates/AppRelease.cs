namespace DtHub.Core.Updates;

/// <summary>
/// A published release, as described by the repository.
/// </summary>
/// <param name="Version">Its version, taken from the tag.</param>
/// <param name="Notes">What changes, written in the release body.</param>
/// <param name="DownloadUrl">The address of the executable.</param>
/// <param name="SizeBytes">
/// Its size, so we can state the wait before enduring it.
/// </param>
/// <param name="DigestUrl">
/// The address of the accompanying digest file. A sixty-megabyte
/// executable that replaces ours is not run on faith after a
/// download: its digest is verified before it is used.
/// </param>
public sealed record AppRelease(
    Version Version,
    string Notes,
    string DownloadUrl,
    long SizeBytes,
    string DigestUrl);

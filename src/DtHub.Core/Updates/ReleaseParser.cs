using DtHub.Core.Localization;

namespace DtHub.Core.Updates;

/// <summary>
/// Reads where the repository sends "releases/latest".
///
/// Separated from the network: this is the part that can get it
/// wrong, and so the part we test.
/// </summary>
public static class ReleaseParser
{
    /// <summary>
    /// The release whose page "releases/latest" lands on, read from the
    /// address alone. GitHub sends that link to the last published
    /// release, drafts and prereleases left out, and to the list when
    /// there is none.
    ///
    /// Asked this way rather than through the API: Kaspersky flags a
    /// program that queries "api.github.com/repos" as using GitHub to
    /// receive orders (NetTool.GitHubGetRepo.HTTP.C&amp;C, 2026-10-10).
    /// The assets sit at fixed addresses beside the tag. The size is
    /// not known, nothing reads it, and the notes come from the
    /// "notes.xx.md" assets, English included.
    /// </summary>
    public static AppRelease? FromTagPage(Uri landed, string assetName)
    {
        ArgumentNullException.ThrowIfNull(landed);
        ArgumentException.ThrowIfNullOrEmpty(assetName);

        var segments = landed.AbsolutePath.Trim('/').Split('/');

        if (segments.Length != 5
            || segments[2] != "releases"
            || segments[3] != "tag"
            || VersionOf(Uri.UnescapeDataString(segments[4])) is not { } version)
        {
            return null;
        }

        var folder = $"{landed.GetLeftPart(UriPartial.Authority)}/{segments[0]}/{segments[1]}/releases/download/{segments[4]}/";

        return new AppRelease(version, string.Empty, folder + assetName, 0, folder + assetName + ".sha256")
        {
            NoteUrls = AppLanguage.Supported.ToDictionary(l => l, l => $"{folder}notes.{l}.md"),
        };
    }

    /// <summary>
    /// The version a tag carries. The customary "v" prefix is
    /// tolerated, the rest must be a number.
    /// </summary>
    public static Version? VersionOf(string? tag)
    {
        var text = (tag ?? string.Empty).Trim();

        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    /// <summary>
    /// A version reduced to its first three numbers, the only ones
    /// the project writes. Without that, "0.2.0" from the repository
    /// and "0.2.0.0" from the assembly do not compare equal, and the
    /// application would believe itself eternally behind itself.
    /// </summary>
    public static Version Normalize(Version? version) =>
        version is null
            ? new Version(0, 0, 0)
            : new Version(
                version.Major,
                version.Minor,
                version.Build < 0 ? 0 : version.Build);
}

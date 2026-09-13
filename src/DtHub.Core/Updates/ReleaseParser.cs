using System.Text.Json;

namespace DtHub.Core.Updates;

/// <summary>
/// Reads what the repository API returns for a release.
///
/// Separated from the network: this is the part that can get it
/// wrong, and so the part we test.
/// </summary>
public static class ReleaseParser
{
    /// <summary>
    /// The release described by this document, or <c>null</c> if it
    /// is not usable.
    ///
    /// Ruled out: a release in draft or marked as a prerelease, a tag
    /// that does not carry a readable version, and a release missing
    /// its executable or its checksum: better to offer nothing than
    /// to offer something we cannot verify.
    /// </summary>
    /// <param name="json">The document returned by the API.</param>
    /// <param name="assetName">The name of the expected executable.</param>
    public static AppRelease? Parse(string? json, string assetName)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetName);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || Flag(root, "draft")
                || Flag(root, "prerelease")
                || VersionOf(Text(root, "tag_name")) is not { } version)
            {
                return null;
            }

            var binary = Asset(root, assetName);
            var digest = Asset(root, assetName + ".sha256");

            return binary is null || digest is null
                ? null
                : new AppRelease(
                    version,
                    Text(root, "body").Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
                    binary.Value.Url,
                    binary.Value.Size,
                    digest.Value.Url);
        }
        catch (JsonException)
        {
            // Silence is intentional: updating is a convenience
            // service, not a dependency. An unreadable response is
            // treated as an absence of version, and the application
            // starts all the same.
            return null;
        }
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

    private static (string Url, long Size)? Asset(JsonElement root, string name)
    {
        if (!root.TryGetProperty("assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(Text(asset, "name"), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = Text(asset, "browser_download_url");

            if (url.Length == 0)
            {
                continue;
            }

            return (url, asset.TryGetProperty("size", out var size)
                && size.TryGetInt64(out var bytes) ? bytes : 0);
        }

        return null;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

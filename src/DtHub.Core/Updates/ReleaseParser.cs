using System.Text.Json;

namespace DtHub.Core.Updates;

/// <summary>
/// Lit ce que l'API du dépôt rend pour une livraison.
///
/// Séparé du réseau : c'est la partie qui peut se tromper, et c'est donc celle
/// qu'on éprouve.
/// </summary>
public static class ReleaseParser
{
    /// <summary>
    /// La livraison décrite par ce document, ou <c>null</c> si elle n'est pas
    /// utilisable.
    ///
    /// Est écartée une livraison en brouillon ou marquée comme essai, une
    /// étiquette qui ne porte pas de version lisible, et une livraison à
    /// laquelle il manque l'exécutable ou son empreinte : mieux vaut ne rien
    /// proposer que proposer ce qu'on ne pourra pas vérifier.
    /// </summary>
    /// <param name="json">Le document rendu par l'API.</param>
    /// <param name="assetName">Le nom de l'exécutable attendu.</param>
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
            // Silence assumé : la mise à jour est un service de confort, non
            // une dépendance. Une réponse illisible se traite comme une absence
            // de version, et l'application démarre pareil.
            return null;
        }
    }

    /// <summary>
    /// La version que porte une étiquette. Le « v » d'usage est toléré, le
    /// reste doit être un numéro.
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
    /// Une version réduite à ses trois premiers nombres, les seuls que le projet
    /// écrit. Sans cela, « 0.2.0 » du dépôt et « 0.2.0.0 » de l'assemblage ne se
    /// comparent pas égaux, et l'application se croirait éternellement en retard
    /// sur elle-même.
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

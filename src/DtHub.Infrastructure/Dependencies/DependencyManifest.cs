using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core.Dependencies;

namespace DtHub.Infrastructure.Dependencies;

/// <summary>
/// Loads <c>build/dependencies.json</c>, embedded as a resource. The
/// same file is used by CI and at runtime: no URL is hardcoded
/// anywhere else in the code.
/// </summary>
public static class DependencyManifest
{
    private const string ResourceName = "DtHub.Infrastructure.dependencies.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<IReadOnlyDictionary<string, ExternalDependency>> Cached =
        new(Load, isThreadSafe: true);

    /// <summary>Key of the dependency providing ADB.</summary>
    public const string PlatformToolsKey = "platform-tools";

    /// <summary>Key of the dependency providing scrcpy.</summary>
    public const string ScrcpyKey = "scrcpy";

    /// <summary>
    /// Key of the macOS build of ADB. The Windows keys above are left
    /// as they are: the PC only ever asks for those.
    /// </summary>
    public const string PlatformToolsMacKey = "platform-tools-macos";

    /// <summary>Key of the macOS (Apple Silicon) build of scrcpy.</summary>
    public const string ScrcpyMacKey = "scrcpy-macos";

    /// <summary>Key of the Linux (x86_64) build of ADB.</summary>
    public const string PlatformToolsLinuxKey = "platform-tools-linux";

    /// <summary>Key of the Linux (x86_64) build of scrcpy.</summary>
    public const string ScrcpyLinuxKey = "scrcpy-linux";

    /// <summary>All declared dependencies, indexed by key.</summary>
    public static IReadOnlyDictionary<string, ExternalDependency> All => Cached.Value;

    /// <summary>Retrieves a declared dependency.</summary>
    /// <exception cref="InvalidOperationException">
    /// The key is not declared in the manifest.
    /// </exception>
    public static ExternalDependency Get(string key) =>
        All.TryGetValue(key, out var dependency)
            ? dependency
            : throw new InvalidOperationException(
                $"La dépendance « {key} » n'est pas déclarée dans build/dependencies.json.");

    private static Dictionary<string, ExternalDependency> Load()
    {
        using var stream = typeof(DependencyManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Ressource « {ResourceName} » absente : build/dependencies.json n'est pas embarqué.");

        var document = JsonSerializer.Deserialize<ManifestDocument>(stream, Options)
            ?? throw new InvalidOperationException("build/dependencies.json est vide ou illisible.");

        return document.Dependencies.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToDependency(pair.Key),
            StringComparer.Ordinal);
    }

    private sealed record ManifestDocument
    {
        [JsonPropertyName("dependencies")]
        public Dictionary<string, ManifestEntry> Dependencies { get; init; } = [];
    }

    private sealed record ManifestEntry
    {
        public string DisplayName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public string Sha256 { get; init; } = string.Empty;
        public string? Sha1 { get; init; }
        public string? ArchiveRootDirectory { get; init; }
        public string Executable { get; init; } = string.Empty;
        public string License { get; init; } = string.Empty;
        public string? LicenseUrl { get; init; }
        public bool Redistributable { get; init; }
        public string? RedistributionNote { get; init; }

        public ExternalDependency ToDependency(string key) => new()
        {
            Key = key,
            DisplayName = DisplayName,
            Version = Version,
            Url = new Uri(Url, UriKind.Absolute),
            SizeBytes = SizeBytes,
            Sha256 = Sha256,
            Sha1 = Sha1,
            ArchiveRootDirectory = ArchiveRootDirectory,
            Executable = Executable,
            License = License,
            LicenseUrl = LicenseUrl,
            Redistributable = Redistributable,
            RedistributionNote = RedistributionNote,
        };
    }
}

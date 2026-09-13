namespace DtHub.Core.Dependencies;

/// <summary>
/// Description of a third-party component downloaded to the user's
/// machine. Each field comes from <c>build/dependencies.json</c>,
/// the only URL source allowed in the project.
/// </summary>
public sealed record ExternalDependency
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }

    /// <summary>
    /// Official, versioned URL, hence with immutable content.
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Expected archive size, the first filter before the digest.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>SHA-256 digest recorded from the official archive.</summary>
    public required string Sha256 { get; init; }

    /// <summary>
    /// SHA-1 digest as published by the vendor, for cross-checking.
    /// </summary>
    public string? Sha1 { get; init; }

    /// <summary>
    /// Root folder contained in the archive, if there is one.
    /// </summary>
    public string? ArchiveRootDirectory { get; init; }

    /// <summary>Main executable, relative to the extracted root.</summary>
    public required string Executable { get; init; }

    public required string License { get; init; }
    public string? LicenseUrl { get; init; }

    /// <summary>False if the license forbids bundling the component.</summary>
    public required bool Redistributable { get; init; }

    public string? RedistributionNote { get; init; }

    /// <summary>
    /// Name of the local install folder, versioned to allow
    /// coexistence.
    /// </summary>
    public string InstallDirectoryName => $"{Key}-{Version}";
}

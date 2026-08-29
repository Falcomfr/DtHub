namespace DtHub.Core.Dependencies;

/// <summary>
/// Description d'un composant tiers téléchargé chez l'utilisateur. Chaque
/// champ vient de <c>build/dependencies.json</c>, seule source d'URL autorisée
/// dans le projet.
/// </summary>
public sealed record ExternalDependency
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }

    /// <summary>URL officielle et versionnée, donc au contenu immuable.</summary>
    public required Uri Url { get; init; }

    /// <summary>Taille attendue de l'archive, premier filtre avant l'empreinte.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Empreinte SHA-256 relevée sur l'archive officielle.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Empreinte SHA-1 telle que publiée par l'éditeur, à titre de recoupement.</summary>
    public string? Sha1 { get; init; }

    /// <summary>Dossier racine contenu dans l'archive, s'il y en a un.</summary>
    public string? ArchiveRootDirectory { get; init; }

    /// <summary>Exécutable principal, relatif à la racine extraite.</summary>
    public required string Executable { get; init; }

    public required string License { get; init; }
    public string? LicenseUrl { get; init; }

    /// <summary>Faux si la licence interdit d'embarquer le composant.</summary>
    public required bool Redistributable { get; init; }

    public string? RedistributionNote { get; init; }

    /// <summary>Nom du dossier d'installation local, versionné pour permettre la coexistence.</summary>
    public string InstallDirectoryName => $"{Key}-{Version}";
}

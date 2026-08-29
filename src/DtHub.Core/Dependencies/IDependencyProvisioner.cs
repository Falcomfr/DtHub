namespace DtHub.Core.Dependencies;

/// <summary>Étape en cours, pour informer l'utilisateur pendant l'attente.</summary>
public enum ProvisioningStage
{
    Downloading,
    Verifying,
    Extracting,
    Done,
}

/// <summary>Avancement d'une mise en place, destiné à l'interface.</summary>
public sealed record ProvisioningProgress(
    ProvisioningStage Stage,
    long BytesReceived = 0,
    long? TotalBytes = null)
{
    /// <summary>Fraction téléchargée, ou <c>null</c> si la taille est inconnue.</summary>
    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1)
        : null;
}

/// <summary>
/// Met en place un composant tiers dans le dossier de données de
/// l'utilisateur : téléchargement depuis la source officielle, vérification de
/// l'empreinte, puis extraction.
/// </summary>
public interface IDependencyProvisioner
{
    /// <summary>
    /// Retourne le chemin absolu de l'exécutable du composant, en le
    /// téléchargeant s'il n'est pas déjà en place.
    /// </summary>
    /// <exception cref="DependencyProvisioningException">
    /// Téléchargement impossible, empreinte non conforme, ou extraction en échec.
    /// </exception>
    Task<string> EnsureAvailableAsync(
        ExternalDependency dependency,
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Chemin de l'exécutable s'il est déjà en place, sans rien télécharger.</summary>
    string? TryGetExistingPath(ExternalDependency dependency);
}

namespace DtHub.Core.Dependencies;

/// <summary>Échec de mise en place d'un composant tiers.</summary>
public sealed class DependencyProvisioningException : Exception
{
    public DependencyProvisioningException(string dependencyKey, string userMessage, Exception? innerException = null)
        : base(userMessage, innerException) => DependencyKey = dependencyKey;

    public DependencyProvisioningException() { }

    public DependencyProvisioningException(string message) : base(message) { }

    public DependencyProvisioningException(string message, Exception innerException)
        : base(message, innerException) { }

    public string? DependencyKey { get; }
}

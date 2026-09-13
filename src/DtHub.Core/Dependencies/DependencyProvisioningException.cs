namespace DtHub.Core.Dependencies;

/// <summary>Failure to set up a third-party component.</summary>
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

namespace DtHub.Core.Adb;

/// <summary>
/// Échec d'une commande ADB. <see cref="UserMessage"/> est destiné à
/// l'interface, <see cref="Exception.Message"/> et <see cref="Details"/> aux
/// journaux. Un code d'appairage ne doit jamais s'y retrouver.
/// </summary>
public sealed class AdbException : Exception
{
    public AdbException(AdbErrorKind kind, string userMessage, string? details = null, Exception? innerException = null)
        : base($"{kind}: {userMessage}", innerException)
    {
        Kind = kind;
        UserMessage = userMessage;
        Details = details;
    }

    public AdbException() => UserMessage = string.Empty;

    public AdbException(string message) : base(message) => UserMessage = message;

    public AdbException(string message, Exception innerException)
        : base(message, innerException) => UserMessage = message;

    public AdbErrorKind Kind { get; }

    /// <summary>Message affichable tel quel dans l'interface.</summary>
    public string UserMessage { get; }

    /// <summary>Sortie technique conservée pour le diagnostic.</summary>
    public string? Details { get; }
}

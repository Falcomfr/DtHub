namespace DtHub.Core.Adb;

/// <summary>
/// Failure of an ADB command. <see cref="UserMessage"/> is intended
/// for the interface, <see cref="Exception.Message"/> and
/// <see cref="Details"/> for the logs. A pairing code must never
/// end up in there.
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

    /// <summary>
    /// Message that can be displayed as is in the interface.
    /// </summary>
    public string UserMessage { get; }

    /// <summary>Technical output kept for diagnostics.</summary>
    public string? Details { get; }
}

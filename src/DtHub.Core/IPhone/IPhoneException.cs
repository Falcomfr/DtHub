namespace DtHub.Core.IPhone;

/// <summary>
/// The iPhone could not be listed or shown. The message is meant for the
/// player and says what to do.
/// </summary>
public sealed class IPhoneException : Exception
{
    public IPhoneException() { }

    public IPhoneException(string message) : base(message) { }

    public IPhoneException(string message, Exception innerException)
        : base(message, innerException) { }
}

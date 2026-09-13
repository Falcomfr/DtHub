namespace DtHub.Core.Processes;

/// <summary>
/// The process could not be started: missing executable,
/// insufficient permissions, invalid path. A nonzero return code
/// does not fall into this category and is reported via
/// <see cref="ProcessResult"/>.
/// </summary>
public sealed class ProcessLaunchException : Exception
{
    public ProcessLaunchException(string fileName, string message, Exception? innerException = null)
        : base(message, innerException) => FileName = fileName;

    public ProcessLaunchException() { }

    public ProcessLaunchException(string message) : base(message) { }

    public ProcessLaunchException(string message, Exception innerException)
        : base(message, innerException) { }

    public string? FileName { get; }
}

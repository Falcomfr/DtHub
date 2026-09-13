namespace DtHub.Core.Processes;

/// <summary>
/// Result of a process that ended, or was killed after the timeout
/// expired.
/// </summary>
public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// True if the process was killed because it exceeded its
    /// timeout.
    /// </summary>
    public bool TimedOut { get; init; }

    public bool Succeeded => !TimedOut && ExitCode == 0;

    /// <summary>
    /// Standard output if it is set, otherwise error output. ADB
    /// writes its failure messages sometimes to one, sometimes to
    /// the other depending on the command, and the caller does not
    /// need to know this detail.
    /// </summary>
    public string OutputOrError =>
        string.IsNullOrWhiteSpace(StandardOutput) ? StandardError : StandardOutput;
}

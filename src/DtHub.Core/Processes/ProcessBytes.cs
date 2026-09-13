namespace DtHub.Core.Processes;

/// <summary>
/// Result of a process whose standard output is binary.
///
/// A separate type, rather than one more field on
/// <see cref="ProcessResult"/>: an output is read as text or as
/// bytes, never both, and a field that is always empty on one of
/// the two paths invites relying on it wrongly.
/// </summary>
public sealed record ProcessBytes
{
    public required int ExitCode { get; init; }

    /// <summary>
    /// The standard output, as is, without decoding or splitting.
    /// </summary>
    public required byte[] StandardOutput { get; init; }

    /// <summary>
    /// The error output, on the other hand, stays text: that is
    /// what it carries.
    /// </summary>
    public required string StandardError { get; init; }

    /// <summary>
    /// True if the process was killed because it exceeded its
    /// timeout.
    /// </summary>
    public bool TimedOut { get; init; }

    public bool Succeeded => !TimedOut && ExitCode == 0 && StandardOutput.Length > 0;
}

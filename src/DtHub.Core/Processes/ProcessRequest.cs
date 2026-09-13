namespace DtHub.Core.Processes;

/// <summary>
/// Description of a process to run. Arguments are passed as a list
/// and never concatenated by the caller: it is the implementation
/// that handles escaping, which prevents any injection through a
/// package name or a path containing spaces.
/// </summary>
public sealed record ProcessRequest
{
    /// <summary>Absolute path of the executable. PATH is never used.</summary>
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>
    /// Delay beyond which the process is killed. <c>null</c> means
    /// none.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>Environment variables added or replaced.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; }
        = new Dictionary<string, string?>();

    /// <summary>
    /// Values to hide in <see cref="ToDisplayString"/>. A pairing
    /// code must never reach a log file.
    /// </summary>
    public IReadOnlyCollection<string> SensitiveValues { get; init; } = [];

    public ProcessRequest() { }

    [SetsRequiredMembers]
    public ProcessRequest(string fileName, params string[] arguments)
    {
        FileName = fileName;
        Arguments = arguments;
    }

    /// <summary>
    /// Readable command line, for logs and diagnostics. Values
    /// declared sensitive are replaced with asterisks there.
    /// </summary>
    public string ToDisplayString() =>
        string.Join(' ', [Quote(FileName), .. Arguments.Select(argument => Quote(Redact(argument)))]);

    private string Redact(string argument) =>
        SensitiveValues.Count > 0 && SensitiveValues.Contains(argument) ? "***" : argument;

    private static string Quote(string value) =>
        value.Length > 0 && !value.Any(char.IsWhiteSpace) ? value : $"\"{value}\"";
}

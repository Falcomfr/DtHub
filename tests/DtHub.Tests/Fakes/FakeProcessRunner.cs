using DtHub.Core.Processes;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated runner: replays ADB output captured on real hardware
/// and records the commands it receives. This is what makes it
/// possible to test the whole ADB layer without a phone.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<Rule> _rules = [];

    /// <summary>Commands received, in order.</summary>
    public List<ProcessRequest> Calls { get; } = [];

    /// <summary>
    /// Arguments of the last command received, joined by spaces.
    /// </summary>
    public string LastArguments => Calls.Count == 0 ? string.Empty : Join(Calls[^1]);

    /// <summary>
    /// Responds as soon as the arguments contain the given sequence.
    /// </summary>
    public FakeProcessRunner Respond(
        string argumentsContain,
        string standardOutput = "",
        string standardError = "",
        int exitCode = 0,
        bool timedOut = false)
    {
        _rules.Add(new Rule(
            request => Join(request).Contains(argumentsContain, StringComparison.Ordinal),
            _ => new ProcessResult
            {
                ExitCode = exitCode,
                StandardOutput = standardOutput,
                StandardError = standardError,
                Duration = TimeSpan.FromMilliseconds(10),
                TimedOut = timedOut,
            }));

        return this;
    }

    /// <summary>
    /// Makes the process fail to start, like a missing ADB.
    /// </summary>
    public FakeProcessRunner FailToLaunch()
    {
        _rules.Add(new Rule(
            _ => true,
            request => throw new ProcessLaunchException(request.FileName, "Exécutable introuvable.")));

        return this;
    }

    private readonly List<BytesRule> _bytesRules = [];

    /// <summary>
    /// Responds in bytes as soon as the arguments contain the given
    /// sequence.
    /// </summary>
    public FakeProcessRunner RespondWithBytes(
        string argumentsContain,
        byte[] standardOutput,
        string standardError = "",
        int exitCode = 0)
    {
        _bytesRules.Add(new BytesRule(
            request => Join(request).Contains(argumentsContain, StringComparison.Ordinal),
            _ => new ProcessBytes
            {
                ExitCode = exitCode,
                StandardOutput = standardOutput,
                StandardError = standardError,
            }));

        return this;
    }

    public Task<ProcessBytes> RunForBytesAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(request);

        var rule = _bytesRules.FirstOrDefault(r => r.Matches(request));

        var result = rule?.Respond(request) ?? new ProcessBytes
        {
            ExitCode = 1,
            StandardOutput = [],
            StandardError = $"FakeProcessRunner : aucune règle binaire pour « {Join(request)} »",
        };

        return Task.FromResult(result);
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(request);

        var rule = _rules.FirstOrDefault(r => r.Matches(request));

        // With no matching rule, we return an explicit failure
        // rather than a silent success that would hide a badly
        // written test.
        var result = rule?.Respond(request) ?? new ProcessResult
        {
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = $"FakeProcessRunner : aucune règle pour « {Join(request)} »",
            Duration = TimeSpan.Zero,
        };

        return Task.FromResult(result);
    }

    private static string Join(ProcessRequest request) => string.Join(' ', request.Arguments);

    private sealed record Rule(Func<ProcessRequest, bool> Matches, Func<ProcessRequest, ProcessResult> Respond);

    private sealed record BytesRule(
        Func<ProcessRequest, bool> Matches,
        Func<ProcessRequest, ProcessBytes> Respond);
}

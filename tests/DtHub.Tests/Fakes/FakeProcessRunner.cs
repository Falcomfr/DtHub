using DtHub.Core.Processes;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Exécuteur simulé : rejoue des sorties ADB relevées sur du vrai matériel et
/// enregistre les commandes reçues. C'est ce qui permet de tester toute la
/// couche ADB sans téléphone.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<Rule> _rules = [];

    /// <summary>Commandes reçues, dans l'ordre.</summary>
    public List<ProcessRequest> Calls { get; } = [];

    /// <summary>Arguments de la dernière commande reçue, joints par des espaces.</summary>
    public string LastArguments => Calls.Count == 0 ? string.Empty : Join(Calls[^1]);

    /// <summary>Répond dès que les arguments contiennent la séquence donnée.</summary>
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

    /// <summary>Fait échouer le démarrage du processus, comme un ADB absent.</summary>
    public FakeProcessRunner FailToLaunch()
    {
        _rules.Add(new Rule(
            _ => true,
            request => throw new ProcessLaunchException(request.FileName, "Exécutable introuvable.")));

        return this;
    }

    private readonly List<BytesRule> _bytesRules = [];

    /// <summary>Répond en octets dès que les arguments contiennent la séquence donnée.</summary>
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

        // Sans règle correspondante, on rend un échec explicite plutôt qu'un
        // succès silencieux qui masquerait un test mal écrit.
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

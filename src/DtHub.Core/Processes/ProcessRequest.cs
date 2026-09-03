namespace DtHub.Core.Processes;

/// <summary>
/// Description d'un processus à exécuter. Les arguments sont passés sous forme
/// de liste et jamais concaténés par l'appelant : c'est l'implémentation qui
/// gère l'échappement, ce qui évite toute injection via un nom de package ou
/// un chemin contenant des espaces.
/// </summary>
public sealed record ProcessRequest
{
    /// <summary>Chemin absolu de l'exécutable. Le PATH n'est jamais utilisé.</summary>
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Délai au-delà duquel le processus est tué. <c>null</c> signifie aucun.</summary>
    public TimeSpan? Timeout { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>Variables d'environnement ajoutées ou remplacées.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; }
        = new Dictionary<string, string?>();

    /// <summary>
    /// Valeurs à masquer dans <see cref="ToDisplayString"/>. Un code
    /// d'appairage ne doit jamais atteindre un fichier de journal.
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
    /// Ligne de commande lisible, pour les journaux et le diagnostic. Les
    /// valeurs déclarées sensibles y sont remplacées par des astérisques.
    /// </summary>
    public string ToDisplayString() =>
        string.Join(' ', [Quote(FileName), .. Arguments.Select(argument => Quote(Redact(argument)))]);

    private string Redact(string argument) =>
        SensitiveValues.Count > 0 && SensitiveValues.Contains(argument) ? "***" : argument;

    private static string Quote(string value) =>
        value.Length > 0 && !value.Any(char.IsWhiteSpace) ? value : $"\"{value}\"";
}

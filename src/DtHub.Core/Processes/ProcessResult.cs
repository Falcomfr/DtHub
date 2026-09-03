namespace DtHub.Core.Processes;

/// <summary>Résultat d'un processus terminé, ou tué après expiration du délai.</summary>
public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>Vrai si le processus a été tué parce qu'il dépassait son délai.</summary>
    public bool TimedOut { get; init; }

    public bool Succeeded => !TimedOut && ExitCode == 0;

    /// <summary>
    /// Sortie standard si elle est renseignée, sinon sortie d'erreur. ADB écrit
    /// ses messages d'échec tantôt sur l'une, tantôt sur l'autre selon la
    /// commande, et l'appelant n'a pas à connaître ce détail.
    /// </summary>
    public string OutputOrError =>
        string.IsNullOrWhiteSpace(StandardOutput) ? StandardError : StandardOutput;
}

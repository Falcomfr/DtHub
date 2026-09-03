namespace DtHub.Core.Processes;

/// <summary>
/// Résultat d'un processus dont la sortie standard est binaire.
///
/// Un type à part, et non un champ de plus sur <see cref="ProcessResult"/> :
/// une sortie se lit en texte ou en octets, jamais dans les deux, et un champ
/// toujours vide sur l'un des deux chemins invite à s'y fier à tort.
/// </summary>
public sealed record ProcessBytes
{
    public required int ExitCode { get; init; }

    /// <summary>La sortie standard, telle quelle, sans décodage ni découpage.</summary>
    public required byte[] StandardOutput { get; init; }

    /// <summary>La sortie d'erreur, elle, reste du texte : c'est ce qu'elle porte.</summary>
    public required string StandardError { get; init; }

    /// <summary>Vrai si le processus a été tué parce qu'il dépassait son délai.</summary>
    public bool TimedOut { get; init; }

    public bool Succeeded => !TimedOut && ExitCode == 0 && StandardOutput.Length > 0;
}

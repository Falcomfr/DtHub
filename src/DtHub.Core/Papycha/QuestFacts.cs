namespace DtHub.Core.Papycha;

/// <summary>
/// Ce que le bloc d'introduction d'une page de quête annonce.
///
/// Ces faits sont engendrés par un bloc maison du site, pas écrits à la main :
/// leur balisage est donc régulier d'une quête à l'autre, contrairement au
/// corps de l'article. C'est ce qui les rend analysables sans deviner.
/// </summary>
public sealed record QuestFacts
{
    /// <summary>Succès auquel la quête appartient, quand elle en a un.</summary>
    public string? Success { get; init; }

    /// <summary>Ce que la chaîne rapporte au bout, par exemple un Dofus.</summary>
    public string? Finality { get; init; }

    /// <summary>Type annoncé : Principale, Alignement Bonta, Événementielle...</summary>
    public string? Type { get; init; }

    /// <summary>Rang de la quête dans son succès, à partir de un.</summary>
    public int StepNumber { get; init; }

    /// <summary>Nombre de quêtes du succès.</summary>
    public int StepCount { get; init; }

    /// <summary>Comment la quête se lance, en une phrase.</summary>
    public string? Start { get; init; }

    /// <summary>Vrai si la quête se situe dans une chaîne connue.</summary>
    public bool HasChain => StepCount > 1 && StepNumber > 0;

    /// <summary>« Étape 3/4 », ou une chaîne vide hors chaîne.</summary>
    public string StepText => HasChain ? $"Étape {StepNumber}/{StepCount}" : string.Empty;
}

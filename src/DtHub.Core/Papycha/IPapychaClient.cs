namespace DtHub.Core.Papycha;

/// <summary>
/// Accès en lecture au site, isolé derrière une interface pour que le noyau
/// reste sans réseau et que le catalogue se vérifie sur des réponses
/// enregistrées.
/// </summary>
public interface IPapychaClient
{
    /// <summary>
    /// Récupère toutes les quêtes, page après page.
    ///
    /// <paramref name="progress"/> reçoit le nombre de quêtes déjà lues et le
    /// total annoncé par le site : l'indexation dure quelques secondes et doit
    /// se voir.
    /// </summary>
    Task<IReadOnlyList<QuestSummary>> GetQuestsAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Récupère les rubriques qui rangent les quêtes.</summary>
    Task<IReadOnlyList<QuestSection>> GetSectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère l'ordre dans lequel le site présente ses rubriques.
    ///
    /// L'API ne le donne pas : elle range les catégories par ordre
    /// alphabétique. Cet ordre-là ne vit que dans le menu du site.
    /// </summary>
    Task<IReadOnlyList<string>> GetSectionOrderAsync(CancellationToken cancellationToken = default);
}

/// <summary>Avancement d'une indexation.</summary>
/// <param name="Loaded">Quêtes déjà lues.</param>
/// <param name="Total">Total annoncé, ou zéro tant qu'il est inconnu.</param>
public readonly record struct QuestIndexingProgress(int Loaded, int Total)
{
    /// <summary>Part accomplie, de zéro à un. Zéro tant que le total manque.</summary>
    public double Fraction => Total > 0 ? Math.Clamp((double)Loaded / Total, 0, 1) : 0;
}

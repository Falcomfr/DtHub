namespace DtHub.Core.Papycha;

/// <summary>
/// Le catalogue tel qu'il est rangé sur le disque.
///
/// C'est un cache, pas un réglage : le perdre ne coûte qu'une nouvelle
/// indexation. Il porte tout de même une version de schéma, pour qu'un
/// changement de forme se solde par une reconstruction et non par une lecture
/// de travers.
/// </summary>
public sealed class QuestCatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Moment de la dernière indexation réussie.</summary>
    public DateTimeOffset? IndexedUtc { get; set; }

    public List<QuestSummary> Quests { get; set; } = [];

    public List<QuestSection> Sections { get; set; } = [];

    /// <summary>
    /// Vrai si le catalogue est inutilisable en l'état et doit être reconstruit.
    /// Un catalogue vieilli reste employé : mieux vaut une liste d'hier que
    /// pas de liste du tout quand le réseau manque.
    /// </summary>
    public bool NeedsRebuild =>
        SchemaVersion != CurrentSchemaVersion || Quests.Count == 0;
}

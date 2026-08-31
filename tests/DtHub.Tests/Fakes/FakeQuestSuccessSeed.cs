using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Carte des succès simulée. Le projet de tests ne lit aucune ressource
/// embarquée : ce que le catalogue en fait se vérifie ici.
/// </summary>
public sealed class FakeQuestSuccessSeed : IQuestSuccessSeed
{
    private readonly Dictionary<string, QuestSeedEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>Rattache une quête à un succès, avec sa place dans la chaîne.</summary>
    public FakeQuestSuccessSeed With(int questId, string success, int chainStep = 0)
    {
        _entries[$"https://exemple.invalid/quete-{questId}"] =
            new QuestSeedEntry(success, chainStep);

        return this;
    }

    public IReadOnlyDictionary<string, QuestSeedEntry> Load() => _entries;
}

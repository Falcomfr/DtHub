using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated achievement map. The test project reads no embedded
/// resource: what the catalog makes of it is verified here.
/// </summary>
public sealed class FakeQuestSuccessSeed : IQuestSuccessSeed
{
    private readonly Dictionary<string, QuestSeedEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Attaches a quest to an achievement, with its place in the chain.
    /// </summary>
    public FakeQuestSuccessSeed With(
        int questId,
        string success,
        int chainStep = 0,
        int playOrder = 0,
        params string[] prerequisites)
    {
        _entries[$"https://exemple.invalid/quete-{questId}"] =
            new QuestSeedEntry(success, chainStep, playOrder, prerequisites);

        return this;
    }

    public IReadOnlyDictionary<string, QuestSeedEntry> Load() => _entries;
}

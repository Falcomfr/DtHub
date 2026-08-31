using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Carte des succès simulée. Le projet de tests ne lit aucune ressource
/// embarquée : ce que le catalogue en fait se vérifie ici.
/// </summary>
public sealed class FakeQuestSuccessSeed : Dictionary<string, string>, IQuestSuccessSeed
{
    public FakeQuestSuccessSeed()
        : base(StringComparer.Ordinal)
    {
    }

    public IReadOnlyDictionary<string, string> Load() => this;
}

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Papycha;

public class EmbeddedQuestCatalogSnapshotTests
{
    [Fact]
    public void Le_catalogue_embarque_contient_les_782_quetes_du_11_octobre()
    {
        var snapshot = new EmbeddedQuestCatalogSnapshot(NullLogger<EmbeddedQuestCatalogSnapshot>.Instance);

        var catalog = snapshot.Load();

        Assert.Equal(782, catalog.Quests.Count);
        Assert.Equal(25, catalog.Sections.Count);
        Assert.Equal(93, catalog.Dungeons.Count);
        Assert.Equal(22, catalog.Paths.Count);
        Assert.Equal(QuestCatalogDocument.CurrentSchemaVersion, catalog.SchemaVersion);
        Assert.Null(catalog.SiteModifiedUtc);
        Assert.False(catalog.NeedsRebuild);
        Assert.Contains(668, catalog.Quests.Single(q => q.Id == 14099).SectionIds);
        Assert.Equal(72, catalog.Quests.Count(q => q.Recurrence == QuestRecurrence.Daily));
    }

    [Fact]
    public void Chaque_lecture_rend_un_catalogue_neuf()
    {
        var snapshot = new EmbeddedQuestCatalogSnapshot(NullLogger<EmbeddedQuestCatalogSnapshot>.Instance);

        var first = snapshot.Load();
        first.Quests.Clear();

        Assert.Equal(782, snapshot.Load().Quests.Count);
    }
}

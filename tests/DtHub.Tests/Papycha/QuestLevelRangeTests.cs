using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class QuestLevelRangeTests
{
    private static QuestSummary Quete(int niveau = 0) =>
        new() { Title = "Une quête", Level = niveau };

    [Fact]
    public void Sans_aucun_niveau_on_ne_dit_rien() =>
        Assert.Null(QuestLevelRange.Of([Quete(), Quete(), Quete()]));

    [Fact]
    public void Sur_une_liste_vide_on_ne_dit_rien() =>
        Assert.Null(QuestLevelRange.Of([]));

    /// <summary>
    /// Two levels out of twenty-three quests do not tell the zone's
    /// range: better to say nothing than to say something
    /// approximate.
    /// </summary>
    [Fact]
    public void Trop_peu_de_niveaux_ne_disent_pas_la_plage() =>
        Assert.Null(QuestLevelRange.Of(
            [Quete(20), Quete(30), .. Enumerable.Range(0, 21).Select(_ => Quete())]));

    /// <summary>
    /// Except when all of them carry it: a zone with two quests
    /// that have a level reported tells an accurate range.
    /// </summary>
    [Fact]
    public void Deux_quetes_toutes_renseignees_disent_leur_plage() =>
        Assert.Equal("niveau 20 - 30", QuestLevelRange.Of([Quete(20), Quete(30)]));

    [Fact]
    public void Un_seul_niveau_ne_se_dit_pas_comme_une_plage() =>
        Assert.Equal("niveau 50", QuestLevelRange.Of([Quete(50), Quete(50), Quete(50)]));

    [Fact]
    public void Une_plage_complete_ne_dit_pas_sur_combien() =>
        Assert.Equal("niveau 20 - 60", QuestLevelRange.Of([Quete(20), Quete(40), Quete(60)]));

    /// <summary>
    /// A partial range reminds how many quests it is based on:
    /// without that it would read as the range of the whole zone.
    /// </summary>
    [Fact]
    public void Une_plage_partielle_dit_sur_combien() =>
        Assert.Equal(
            "niveau 20 - 60 (sur 3)",
            QuestLevelRange.Of([Quete(20), Quete(40), Quete(60), Quete(), Quete()]));
}

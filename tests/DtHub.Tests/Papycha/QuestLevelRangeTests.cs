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
    /// Deux niveaux sur vingt-trois quêtes ne disent pas la plage de la zone :
    /// mieux vaut ne rien dire que dire à peu près.
    /// </summary>
    [Fact]
    public void Trop_peu_de_niveaux_ne_disent_pas_la_plage() =>
        Assert.Null(QuestLevelRange.Of(
            [Quete(20), Quete(30), .. Enumerable.Range(0, 21).Select(_ => Quete())]));

    /// <summary>
    /// Sauf quand elles le portent toutes : une zone de deux quêtes renseignées
    /// dit une plage juste.
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
    /// Une plage partielle rappelle sur combien de quêtes elle repose : sans
    /// cela elle se lirait comme la plage de toute la zone.
    /// </summary>
    [Fact]
    public void Une_plage_partielle_dit_sur_combien() =>
        Assert.Equal(
            "niveau 20 - 60 (sur 3)",
            QuestLevelRange.Of([Quete(20), Quete(40), Quete(60), Quete(), Quete()]));
}

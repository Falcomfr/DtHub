using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class DungeonLevelBandTests
{
    [Theory]
    // Les bornes sont le seul endroit où l'on se trompe : cinquante ferme le
    // premier palier, cinquante et un ouvre le second.
    [InlineData(1, "Niveau 1 à 50")]
    [InlineData(12, "Niveau 1 à 50")]
    [InlineData(50, "Niveau 1 à 50")]
    [InlineData(51, "Niveau 51 à 100")]
    [InlineData(100, "Niveau 51 à 100")]
    [InlineData(101, "Niveau 101 à 150")]
    [InlineData(150, "Niveau 101 à 150")]
    [InlineData(151, "Niveau 151 à 200")]
    [InlineData(200, "Niveau 151 à 200")]
    public void Chaque_niveau_tombe_dans_son_palier(int niveau, string attendu) =>
        Assert.Equal(attendu, DungeonLevelBand.NameOf(niveau));

    [Fact]
    public void Les_paliers_se_suivent_dans_l_ordre_des_niveaux()
    {
        Assert.True(DungeonLevelBand.RankOf(50) < DungeonLevelBand.RankOf(51));
        Assert.True(DungeonLevelBand.RankOf(150) < DungeonLevelBand.RankOf(200));
    }

    [Fact]
    public void Un_donjon_sans_niveau_ferme_la_marche()
    {
        // Trois donjons sur quatre-vingt-trois n'ont pas de niveau renseigné :
        // les ranger au niveau zéro les mettrait en tête, ce qui serait faux.
        Assert.Equal(DungeonLevelBand.Unknown, DungeonLevelBand.RankOf(0));
        Assert.Equal("Niveau inconnu", DungeonLevelBand.NameOf(0));
        Assert.True(DungeonLevelBand.RankOf(0) > DungeonLevelBand.RankOf(200));
    }
}

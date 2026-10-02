using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The dungeon list, cut into bands of fifty levels, each row carrying its
/// three facts in full: the size lives on the row, where it is read, and the
/// header only names the band.
/// </summary>
public class DungeonBandTests
{
    private static DungeonSummary Donjon(string title, int level, string size = "gigantesque pierre d’âme") => new()
    {
        Title = title,
        Level = level,
        SoulStone = size,
        Position = "[7,-25]",
    };

    [Fact]
    public void L_en_tete_ne_nomme_que_la_tranche()
    {
        var nodes = QuestTree.Banded([Donjon("A", 190), Donjon("B", 200)]).ToList();

        Assert.Equal("Niveau 151 à 200", nodes[0].Label);
    }

    [Fact]
    public void Chaque_ligne_garde_sa_taille()
    {
        var nodes = QuestTree.Banded([Donjon("A", 190), Donjon("B", 200)]).Skip(1);

        Assert.All(nodes, n => Assert.Equal("gigantesque", n.Facts!.Size));
    }

    [Fact]
    public void Les_tranches_restent_dans_l_ordre_des_niveaux()
    {
        var nodes = QuestTree.Banded([Donjon("Haut", 200), Donjon("Sans", 0), Donjon("Bas", 12)]);

        Assert.Equal(
            ["Niveau 1 à 50", "Bas", "Niveau 151 à 200", "Haut", "Niveau inconnu", "Sans"],
            nodes.Select(n => n.Label));
    }
}

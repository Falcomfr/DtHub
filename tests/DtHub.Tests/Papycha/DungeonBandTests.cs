using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The soul stone follows the level: thirty dungeons out of thirty say
/// "gigantesque" between 151 and 200. Repeated on every line it was the
/// heaviest column and said nothing, so the band header says it once and
/// a line only speaks when it differs.
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
    public void L_en_tete_dit_la_taille_partagee_par_la_tranche()
    {
        var nodes = QuestTree.Banded([Donjon("A", 190), Donjon("B", 200)]).ToList();

        Assert.Equal("Niveau 151 à 200 · pierre gigantesque", nodes[0].Label);
    }

    [Fact]
    public void Une_ligne_tait_la_taille_de_sa_tranche_et_son_unite()
    {
        var ligne = QuestTree.Banded([Donjon("A", 190), Donjon("B", 200)]).ElementAt(1);

        Assert.Equal(string.Empty, ligne.Facts!.Size);
        Assert.Equal("190", ligne.Facts.Level);
    }

    [Fact]
    public void Une_exception_garde_sa_taille()
    {
        var nodes = QuestTree.Banded(
        [
            Donjon("A", 60, "moyenne pierre d’âme"),
            Donjon("B", 70, "moyenne pierre d’âme"),
            Donjon("C", 80),
        ]).ToList();

        Assert.Equal("Niveau 51 à 100 · pierre moyenne", nodes[0].Label);
        Assert.Equal("gigantesque", nodes.Single(n => n.Label == "C").Facts!.Size);
    }

    /// <summary>
    /// The levelless band holds one "grande" and one "moyenne": naming
    /// either in the header would mislabel the other.
    /// </summary>
    [Fact]
    public void Sans_majorite_l_en_tete_ne_dit_aucune_taille()
    {
        var nodes = QuestTree.Banded(
        [
            Donjon("A", 0, "grande pierre d’âme"),
            Donjon("B", 0, "moyenne pierre d’âme"),
        ]).ToList();

        Assert.Equal("Niveau inconnu", nodes[0].Label);
        Assert.Equal(["grande", "moyenne"], nodes.Skip(1).Select(n => n.Facts!.Size));
    }

    [Fact]
    public void Les_tranches_restent_dans_l_ordre_des_niveaux()
    {
        var nodes = QuestTree.Banded([Donjon("Haut", 200), Donjon("Sans", 0), Donjon("Bas", 12)]);

        Assert.Equal(
            ["Bas", "Haut", "Sans"],
            nodes.Where(n => n.Kind == QuestNodeKind.Quest).Select(n => n.Label));
    }

    /// <summary>
    /// Search results, raids and lairs have no band above them: a bare
    /// "200" would mean nothing there.
    /// </summary>
    [Fact]
    public void Hors_tranche_une_ligne_garde_tout()
    {
        var facts = QuestTree.NodeOf(Donjon("A", 200)).Facts!;

        Assert.Equal("niv. 200", facts.Level);
        Assert.Equal("gigantesque", facts.Size);
    }
}

using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class DungeonFactsTests
{
    private static DungeonSummary Donjon(int level = 30, string size = "petite") => new()
    {
        Title = "Donjon des Champs",
        Level = level,
        SoulStone = size,
        Position = "[7,-25]",
    };

    /// <summary>
    /// The split this whole change rests on. As long as the level was
    /// formatted into the label, the name and the level were one run of
    /// text and neither could be aligned or weighted on its own.
    /// </summary>
    [Fact]
    public void Le_libelle_d_un_donjon_ne_porte_plus_son_niveau()
    {
        var node = QuestTree.NodeOf(Donjon());

        Assert.Equal("Donjon des Champs", node.Label);
        Assert.DoesNotContain("niv.", node.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void Les_faits_separent_niveau_taille_et_position()
    {
        var facts = DungeonFacts.Of(Donjon());

        Assert.Equal("niv. 30", facts.Level);
        Assert.Equal("petite", facts.Size);
        Assert.Equal("[7,-25]", facts.Position);
    }

    /// <summary>
    /// Three of the eighty-three dungeons have no level on the site.
    /// They sit under their own "Niveau inconnu" band, which already
    /// states the absence: saying it again in the cell would say it
    /// twice.
    /// </summary>
    [Fact]
    public void Un_donjon_sans_niveau_n_annonce_aucun_niveau()
    {
        Assert.Equal(string.Empty, DungeonFacts.Of(Donjon(level: 0)).Level);
    }

    /// <summary>
    /// "gigantesque pierre d'âme" says "pierre d'âme" twice in a column
    /// where every line already carries one. The rule lived in the
    /// joined detail string and had to survive the split.
    /// </summary>
    [Fact]
    public void La_taille_ne_repete_pas_la_pierre_d_ame()
    {
        Assert.Equal(
            "gigantesque",
            DungeonFacts.Of(Donjon(size: "gigantesque pierre d’âme")).Size);
    }

    [Fact]
    public void Un_donjon_porte_ses_faits_jusqu_au_noeud()
    {
        Assert.NotNull(QuestTree.NodeOf(Donjon()).Facts);
    }

    /// <summary>
    /// Six of the seven kinds of row have no facts, and the template
    /// lays its fixed columns out only for those that do.
    /// </summary>
    [Fact]
    public void Une_quete_ne_porte_pas_de_faits()
    {
        Assert.Null(new QuestNode(QuestNodeKind.Quest, "Une quête").Facts);
    }
}

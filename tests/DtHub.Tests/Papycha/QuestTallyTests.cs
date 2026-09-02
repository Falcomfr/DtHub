using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class QuestTallyTests
{
    [Fact]
    public void Une_premiere_lecture_ne_se_compare_a_rien() =>
        Assert.Equal(
            string.Empty,
            new QuestTally(782, 83, 21).Since(new QuestTally(0, 0, 0)));

    [Fact]
    public void Une_relecture_sans_changement_se_tait() =>
        Assert.Equal(
            string.Empty,
            new QuestTally(782, 83, 21).Since(new QuestTally(782, 83, 21)));

    [Fact]
    public void Trois_quetes_de_plus() =>
        Assert.Equal(
            "Guides relus : 3 quêtes de plus.",
            new QuestTally(785, 83, 21).Since(new QuestTally(782, 83, 21)));

    [Fact]
    public void Une_seule_quete_s_accorde() =>
        Assert.Equal(
            "Guides relus : 1 quête de plus.",
            new QuestTally(783, 83, 21).Since(new QuestTally(782, 83, 21)));

    /// <summary>Le site retire aussi des pages : ce n'est pas une raison de se taire.</summary>
    [Fact]
    public void Une_perte_se_dit_comme_un_gain() =>
        Assert.Equal(
            "Guides relus : 2 quêtes de moins.",
            new QuestTally(780, 83, 21).Since(new QuestTally(782, 83, 21)));

    [Fact]
    public void Les_trois_familles_se_disent_dans_l_ordre() =>
        Assert.Equal(
            "Guides relus : 1 quête de plus, 2 lieux de combat de moins, 1 chemin de plus.",
            new QuestTally(783, 81, 22).Since(new QuestTally(782, 83, 21)));

    /// <summary>
    /// Un lieu de combat perdu et un gagné laissent le compte inchangé, et il
    /// n'y a donc rien à annoncer : c'est le compte qu'on rapporte, pas le
    /// remaniement.
    /// </summary>
    [Fact]
    public void Ce_qui_ne_bouge_pas_ne_se_dit_pas() =>
        Assert.Equal(
            "Guides relus : 1 quête de plus.",
            new QuestTally(783, 83, 21).Since(new QuestTally(782, 83, 21)));

    [Fact]
    public void Le_releve_d_un_catalogue_compte_les_trois_familles()
    {
        var tally = QuestTally.Of(new QuestCatalogDocument
        {
            Quests = [new QuestSummary { Title = "Une quête" }],
            Dungeons = [new DungeonSummary { Title = "Un donjon" }, new DungeonSummary()],
            Paths = [],
        });

        Assert.Equal(new QuestTally(1, 2, 0), tally);
    }
}

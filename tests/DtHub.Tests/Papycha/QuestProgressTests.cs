using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The count shown at the foot of a guide. The window used to build
/// it from the catalogue alone, and six of the one hundred and
/// fifteen achievements came out right.
/// </summary>
public sealed class QuestProgressTests
{
    private static QuestFacts Page(int rang, int total) =>
        new() { StepNumber = rang, StepCount = total };

    private static QuestNeighbours Catalogue(int rang, int total) =>
        new(null, null, rang, total);

    [Fact]
    public void Le_total_est_celui_que_la_page_publie_et_non_celui_du_catalogue()
    {
        // "À la barbe du roi" : la page annonce l'étape 9 sur 11, le
        // catalogue n'en connaît que cinq.
        var montre = QuestProgress.Of(Page(9, 11), hasSuccess: true, Catalogue(3, 5));

        Assert.Equal((9, 11), montre);
    }

    [Fact]
    public void Un_succes_de_trois_quetes_se_lit_trois_sur_trois()
    {
        // "Le théâtre des gobelins" : six quêtes nomment ce succès,
        // le succès en compte trois.
        var montre = QuestProgress.Of(Page(3, 3), hasSuccess: true, Catalogue(4, 6));

        Assert.Equal((3, 3), montre);
    }

    [Fact]
    public void Une_quete_sans_succes_est_une_sur_une()
    {
        var montre = QuestProgress.Of(Page(0, 0), hasSuccess: false, Catalogue(0, 0));

        Assert.Equal((1, 1), montre);
    }

    [Fact]
    public void Une_quete_sans_succes_ne_compte_pas_dans_le_succes_precedent()
    {
        // On arrive de la dernière d'un succès, et ses voisines sont
        // encore celles que le catalogue avait posées.
        var montre = QuestProgress.Of(Page(0, 0), hasSuccess: false, Catalogue(3, 3));

        Assert.Equal((1, 1), montre);
    }

    [Fact]
    public void Tant_que_la_page_n_est_pas_arrivee_le_catalogue_tient_le_compte()
    {
        var montre = QuestProgress.Of(null, hasSuccess: true, Catalogue(2, 4));

        Assert.Equal((2, 4), montre);
    }

    [Fact]
    public void Sans_page_et_sans_succes_il_n_y_a_rien_a_montrer()
    {
        Assert.Null(QuestProgress.Of(null, hasSuccess: false, Catalogue(0, 0)));
    }

    [Fact]
    public void Une_page_muette_laisse_parler_le_catalogue()
    {
        // Six succès sur cent quinze ne publient aucune progression.
        var montre = QuestProgress.Of(Page(0, 0), hasSuccess: true, Catalogue(2, 3));

        Assert.Equal((2, 3), montre);
    }

    [Fact]
    public void Une_page_muette_sur_un_succes_que_le_catalogue_ignore_ne_montre_rien()
    {
        Assert.Null(QuestProgress.Of(Page(0, 0), hasSuccess: true, Catalogue(0, 0)));
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, 5)]
    public void Un_rang_sans_total_ou_un_total_sans_rang_ne_dit_rien(int rang, int total)
    {
        // La page les donne dans la même phrase : l'un sans l'autre
        // est une lecture ratée, pas une progression.
        var montre = QuestProgress.Of(Page(rang, total), hasSuccess: true, Catalogue(2, 3));

        Assert.Equal((2, 3), montre);
    }

    [Fact]
    public void Le_rang_ne_depasse_jamais_le_total()
    {
        var montre = QuestProgress.Of(Page(9, 3), hasSuccess: true, Catalogue(1, 1));

        Assert.Equal((9, 9), montre);
    }
}

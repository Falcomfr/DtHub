using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestStepLabelTests
{
    [Fact]
    public void Une_consigne_ne_montre_rien()
    {
        // Le rang suffit à s'y rendre, et la page a le paragraphe en entier
        // juste au-dessus.
        var label = QuestStepLabel.For(
            new QuestStep("Rendez-vous en [4,-19] et parlez à Aida Limenterre.", IsTitle: false),
            isDeparture: false,
            departure: "Rendez-vous en [4,-19], parlez à Aida Limenterre.");

        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void Un_titre_de_section_est_rendu_tel_quel()
    {
        // Le résumer lui ajoutait une majuscule et un point final : « Les
        // salles » s'affichait « Les salles. »
        var label = QuestStepLabel.For(
            new QuestStep("Les salles", IsTitle: true),
            isDeparture: false,
            departure: null);

        Assert.Equal("Les salles", label);
    }

    [Fact]
    public void Le_depart_montre_ce_que_les_metadonnees_disent()
    {
        var label = QuestStepLabel.For(
            new QuestStep("Cette quête se lance automatiquement.", IsTitle: false),
            isDeparture: true,
            departure: "Rendez-vous en [4,-6], parlez à Yse Vewibad.");

        Assert.Equal("Rendez-vous en [4,-6], parlez à Yse Vewibad.", label);
    }

    [Fact]
    public void Un_depart_que_le_site_ne_renseigne_pas_ne_montre_rien()
    {
        // Et surtout pas la prose de l'étape à sa place : c'est ce que le
        // résumé faisait, et c'est ce qu'on retire.
        Assert.Equal(
            string.Empty,
            QuestStepLabel.For(
                new QuestStep("Cette quête est répétable.", IsTitle: false),
                isDeparture: true,
                departure: null));
    }

    [Fact]
    public void Le_depart_d_une_fiche_de_lieu_remplace_une_etape_sans_texte()
    {
        // Sur une fiche de lieu, la première étape est le départ et son texte
        // est vide : c'est la fenêtre qui le compose des métadonnées du lieu.
        var label = QuestStepLabel.For(
            new QuestStep(string.Empty, IsTitle: false),
            isDeparture: true,
            departure: "Rendez-vous en [9,-57], parlez à Bilby Tueur.");

        Assert.Equal("Rendez-vous en [9,-57], parlez à Bilby Tueur.", label);
    }

    [Fact]
    public void Un_titre_de_section_ne_cede_pas_la_place_a_la_ligne_de_depart()
    {
        // Le pont annonce un départ dès que la page porte un bloc de départ ou
        // qu'elle est une fiche de lieu, mais il ne pousse une étape de départ
        // que dans le second cas. Deux guides du site ont les deux, des titres
        // et un bloc de départ : « La voie du Wukang / La voie du Wukin » et
        // « L'éternelle moisson ». Leur premier titre était remplacé par la
        // ligne de départ, qui annonçait un lieu où la page n'était pas.
        var label = QuestStepLabel.For(
            new QuestStep("Liste des Monstres", IsTitle: true),
            isDeparture: true,
            departure: "Rendez-vous en [4,-6], parlez à Yse Vewibad.");

        Assert.Equal("Liste des Monstres", label);
    }

    [Fact]
    public void Le_depart_ne_compte_pas_dans_le_total()
    {
        // Cent quatre-vingt-deux des sept cent quatre-vingt-deux guides du site
        // n'ont qu'une consigne. En comptant le départ, ils annonçaient
        // « Étape 1 / 2 » pour une seule chose à faire.
        Assert.Null(QuestStepLabel.Numbering(0, count: 2, hasDeparture: true));
        Assert.Equal((1, 1), QuestStepLabel.Numbering(1, count: 2, hasDeparture: true));
    }

    [Fact]
    public void Sans_depart_toutes_les_etapes_se_comptent()
    {
        // Une fiche de chemin n'a pas de départ : ses titres sont des étapes.
        Assert.Equal((1, 3), QuestStepLabel.Numbering(0, count: 3, hasDeparture: false));
        Assert.Equal((3, 3), QuestStepLabel.Numbering(2, count: 3, hasDeparture: false));
    }

    [Fact]
    public void Un_guide_sans_consigne_ne_numerote_rien()
    {
        // Trente guides du site sont dans ce cas : le départ, et rien d'autre.
        Assert.Null(QuestStepLabel.Numbering(0, count: 1, hasDeparture: true));
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, 3)]
    [InlineData(9, 3)]
    public void Un_rang_hors_bornes_ne_numerote_rien(int index, int count)
    {
        Assert.Null(QuestStepLabel.Numbering(index, count, hasDeparture: false));
    }

    [Fact]
    public void Un_titre_en_tete_n_est_jamais_le_depart()
    {
        // Les deux guides à titres et bloc de départ : le pont annonce un
        // départ, mais la première étape est un titre. Le rang et le libellé
        // doivent en juger pareil, sans quoi l'un nommerait « Départ » ce que
        // l'autre affiche comme un titre.
        Assert.False(QuestStepLabel.IsDeparture(
            new QuestStep("Liste des Monstres", IsTitle: true), isFirst: true, startsAtDeparture: true));

        Assert.True(QuestStepLabel.IsDeparture(
            new QuestStep(string.Empty, IsTitle: false), isFirst: true, startsAtDeparture: true));

        Assert.False(QuestStepLabel.IsDeparture(
            new QuestStep("Parlez à Otomaï", IsTitle: false), isFirst: false, startsAtDeparture: true));
    }
}

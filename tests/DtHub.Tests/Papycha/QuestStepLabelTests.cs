using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestStepLabelTests
{
    [Fact]
    public void Une_consigne_ne_montre_rien()
    {
        // The rank alone is enough to reach it, and the page has the
        // full paragraph right above it.
        var label = QuestStepLabel.For(
            new QuestStep("Rendez-vous en [4,-19] et parlez à Aida Limenterre.", IsTitle: false),
            isDeparture: false,
            departure: "Rendez-vous en [4,-19], parlez à Aida Limenterre.");

        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void Un_titre_de_section_est_rendu_tel_quel()
    {
        // Summarizing it used to add a capital letter and a final
        // period: "Les salles" was displayed as "Les salles."
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
        // And especially not the step's prose in its place: that is
        // what the summary used to do, and that is what we remove.
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
        // On a place sheet, the first step is the departure and its
        // text is empty: it is the window that builds it from the
        // place's metadata.
        var label = QuestStepLabel.For(
            new QuestStep(string.Empty, IsTitle: false),
            isDeparture: true,
            departure: "Rendez-vous en [9,-57], parlez à Bilby Tueur.");

        Assert.Equal("Rendez-vous en [9,-57], parlez à Bilby Tueur.", label);
    }

    [Fact]
    public void Un_titre_de_section_ne_cede_pas_la_place_a_la_ligne_de_depart()
    {
        // The bridge announces a departure as soon as the page carries
        // a departure block or is a place sheet, but it only pushes a
        // departure step in the second case. Two guides on the site
        // have both, titles and a departure block: "La voie du Wukang /
        // La voie du Wukin" and "L'éternelle moisson". Their first
        // title used to be replaced by the departure line, which
        // announced a place the page was not about.
        var label = QuestStepLabel.For(
            new QuestStep("Liste des Monstres", IsTitle: true),
            isDeparture: true,
            departure: "Rendez-vous en [4,-6], parlez à Yse Vewibad.");

        Assert.Equal("Liste des Monstres", label);
    }

    [Fact]
    public void Le_depart_ne_compte_pas_dans_le_total()
    {
        // 182 of the site's 782 guides have only one instruction. When
        // counting the departure, they used to announce "Étape 1 / 2"
        // ("Step 1 / 2") for a single thing to do.
        Assert.Null(QuestStepLabel.Numbering(0, count: 2, hasDeparture: true));
        Assert.Equal((1, 1), QuestStepLabel.Numbering(1, count: 2, hasDeparture: true));
    }

    [Fact]
    public void Sans_depart_toutes_les_etapes_se_comptent()
    {
        // A path sheet has no departure: its titles are steps.
        Assert.Equal((1, 3), QuestStepLabel.Numbering(0, count: 3, hasDeparture: false));
        Assert.Equal((3, 3), QuestStepLabel.Numbering(2, count: 3, hasDeparture: false));
    }

    [Fact]
    public void Un_guide_sans_consigne_ne_numerote_rien()
    {
        // Thirty guides on the site are in this case: the departure,
        // and nothing else.
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
        // The two guides with titles and a departure block: the bridge
        // announces a departure, but the first step is a title. The
        // rank and the label must agree on this, or one would call it
        // "Départ" ("Departure") while the other displays it as a
        // title.
        Assert.False(QuestStepLabel.IsDeparture(
            new QuestStep("Liste des Monstres", IsTitle: true), isFirst: true, startsAtDeparture: true));

        Assert.True(QuestStepLabel.IsDeparture(
            new QuestStep(string.Empty, IsTitle: false), isFirst: true, startsAtDeparture: true));

        Assert.False(QuestStepLabel.IsDeparture(
            new QuestStep("Parlez à Otomaï", IsTitle: false), isFirst: false, startsAtDeparture: true));
    }
}

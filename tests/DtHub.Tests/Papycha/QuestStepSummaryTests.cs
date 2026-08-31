using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestStepSummaryTests
{
    [Fact]
    public void Une_etape_verbeuse_se_ramene_a_ou_aller_et_a_qui_parler()
    {
        // Texte relevé sur « Le guide du Roublard », étape 2.
        const string brut =
            "Pour lancer la quête, rendez vous au Château d’Amakna en [4,-6] "
            + "pour parler à Yse Vewibad :";

        Assert.Equal("Rendez-vous en [4,-6], parlez à Yse Vewibad.", QuestStepSummary.Of(brut));
    }

    [Fact]
    public void Une_etape_deja_courte_et_bien_tournee_est_gardee()
    {
        // La recomposer perdrait « touchez la tombe », qui est l'essentiel.
        const string brut = "Rendez-vous en [0,-11], touchez la tombe du Chevalier de l’Automne.";

        Assert.Equal(brut, QuestStepSummary.Of(brut));
    }

    [Fact]
    public void Le_nom_du_personnage_n_avale_pas_le_mot_suivant()
    {
        // La capture s'arrête à la ponctuation, pas au sens : sans garde-fou
        // elle rendait « Milicien Kâpon en ».
        const string brut =
            "La quête se lance auprès du Milicien Kâpon en [-1,-12], dans le champs "
            + "du repos. Ce dernier vous invite à aller inspecter les tombes.";

        Assert.Equal("Rendez-vous en [-1,-12], parlez à Milicien Kâpon.", QuestStepSummary.Of(brut));
    }

    [Fact]
    public void Une_etape_sans_repere_garde_sa_premiere_phrase()
    {
        // Une étape narrative ne se résume pas. Ne rien afficher laisserait
        // croire qu'il n'y a rien à faire : on garde la première phrase.
        const string brut =
            "Vous vous penchez pour ramasser l’objet égaré qui brille entre les hautes "
            + "herbes. II s’agit d’un long couteau sacrificiel couvert de boue.";

        Assert.Equal(
            "Vous vous penchez pour ramasser l’objet égaré qui brille entre les hautes herbes.",
            QuestStepSummary.Of(brut));
    }

    [Fact]
    public void Une_phrase_trop_longue_est_coupee_sur_un_mot()
    {
        var brut = "Il se passe " + new string('a', 200) + " des choses.";

        var resume = QuestStepSummary.Of(brut);

        Assert.True(resume.Length <= 112, resume.Length.ToString(TestCulture));
        Assert.EndsWith("…", resume, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_texte_vide_ne_donne_rien(string? brut)
    {
        Assert.Equal(string.Empty, QuestStepSummary.Of(brut));
    }

    [Theory]
    [InlineData("[4,-6]", "Yse Vewibad", "Rendez-vous en [4,-6], parlez à Yse Vewibad.")]
    [InlineData("[-29,-47]", null, "Rendez-vous en [-29,-47].")]
    [InlineData(null, "Maire Cantile", "Parlez à Maire Cantile.")]
    public void Le_depart_se_compose_des_metadonnees(string? position, string? qui, string attendu)
    {
        // Bien plus sûr que la lecture de la prose : le site renseigne la
        // position sur 687 quêtes sur 782 et le personnage sur 693.
        Assert.Equal(attendu, QuestStepSummary.OfStart(position, qui));
    }

    [Fact]
    public void Un_depart_que_le_site_ne_renseigne_pas_ne_donne_rien()
    {
        Assert.Null(QuestStepSummary.OfStart(null, null));
        Assert.Null(QuestStepSummary.OfStart("", "  "));
    }

    private static System.Globalization.CultureInfo TestCulture =>
        System.Globalization.CultureInfo.InvariantCulture;
}

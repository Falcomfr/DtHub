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

    // ------------------------------------------------------------------
    // Le nom capturé déborde : seize cas sur dix-huit relevés sur le site.
    // ------------------------------------------------------------------

    [Theory]
    // Aucune ponctuation ne borne le nom, et la capture prenait la fin de la
    // phrase, coupée net au quarantième caractère.
    [InlineData(
        "La quête se lance à la suite de la quête précédente en récupérant la panoplie "
        + "honorifique d’Albuera auprès du Grand jarl Ordyn et en vous mettant en route "
        + "pour l’île de Belladone.",
        "Parlez à Grand jarl Ordyn.")]
    [InlineData(
        "Une fois le dernier combat terminé, parlez de nouveau à Lave Azza pour sortir "
        + "du donjon et valider la quête ! Il vous faudra ensuite ressortir du donjon.",
        "Parlez à Lave Azza.")]
    [InlineData(
        "Rendez-vous ensuite en [9,-57] et parlez au Gardien du Donjon de Belladone en "
        + "lui montrant la clef que vous venez d’obtenir auprès de Dame Belladone.",
        "Rendez-vous en [9,-57], parlez à Gardien du Donjon de Belladone.")]
    [InlineData(
        "Retournez voir Jin Spyr qui vous félicite pour votre courage et vous remet "
        + "votre récompense avant de repartir vers le village.",
        "Parlez à Jin Spyr.")]
    [InlineData(
        "Vous devez ensuite parler au Sorcier changeur de classe pour changer de classe "
        + "et poursuivre votre apprentissage auprès des maîtres du Kanojedo.",
        "Parlez à Sorcier changeur de classe.")]
    public void Le_nom_s_arrete_au_premier_mot_qui_ouvre_autre_chose(string brut, string attendu) =>
        Assert.Equal(attendu, QuestStepSummary.Of(brut));

    [Fact]
    public void Une_particule_ne_termine_jamais_un_nom()
    {
        const string brut =
            "Une fois tous les monstres vaincus, retournez voir le Gardien du Donjon de "
            + "pour obtenir la clef et terminer la quête sans plus attendre.";

        Assert.Equal("Parlez à Gardien du Donjon.", QuestStepSummary.Of(brut));
    }

    // ------------------------------------------------------------------
    // Les amorces relevées sur le site, et celles qu'on refuse.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(
        "La quête se lance à la suite de la précédente dans la Taverne en [6,-59] en "
        + "faisant vos adieux à Jin Spyr après avoir vaincu Belladone une bonne fois.",
        "Rendez-vous en [6,-59], parlez à Jin Spyr.")]
    [InlineData(
        "La quête se lance automatiquement en parlant à Truffo lors de votre première "
        + "visite du donjon de la Serre du Royalmouth, sur l’île de Frigost.",
        "Parlez à Truffo.")]
    [InlineData(
        "Une fois le combat terminé et le donjon achevé, reparlez à Richard Cassetout "
        + "afin de lui remettre la pièce et de recevoir votre récompense.",
        "Parlez à Richard Cassetout.")]
    [InlineData(
        "Après avoir traversé toute la zone, présentez-vous à Maître Jedo pour entamer "
        + "votre apprentissage et découvrir ce que le Kanojedo attend de vous.",
        "Parlez à Maître Jedo.")]
    public void Les_amorces_relevees_sur_le_site_sont_reconnues(string brut, string attendu) =>
        Assert.Equal(attendu, QuestStepSummary.Of(brut));

    [Theory]
    // « à » suivi d'une majuscule annonce aussi bien un lieu : les accepter
    // ferait passer Astrub pour quelqu'un à qui l'on parle.
    [InlineData(
        "Le bateau vous emmène à Astrub, où vous attend la suite de votre voyage, et "
        + "vous y débarquez sans avoir besoin de rien préparer de particulier.")]
    [InlineData(
        "Une fois que vous êtes à Albuera, la quête se poursuit d’elle-même et vous "
        + "n’avez plus qu’à suivre l’indicateur affiché sur votre carte du monde.")]
    public void Un_lieu_ne_passe_pas_pour_un_personnage(string brut) =>
        Assert.DoesNotContain("parlez à", QuestStepSummary.Of(brut), StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // Aucune sortie ne finit au milieu d'un mot.
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // Le départ, composé des métadonnées du site.
    // ------------------------------------------------------------------

    [Fact]
    public void Le_depart_nomme_le_personnage_et_le_lieu() =>
        Assert.Equal(
            "Rendez-vous en [7,-59], parlez à Jin Spyr.",
            QuestStepSummary.OfStart("[7,-59]", "Jin Spyr"));

    [Fact]
    public void Un_nom_de_depart_a_particules_reste_entier() =>
        Assert.Equal(
            "Parlez à Guichetier de la foire du Trool.",
            QuestStepSummary.OfStart(null, "Guichetier de la foire du Trool"));

    [Fact]
    public void Un_article_devant_le_nom_de_depart_ne_le_disqualifie_pas() =>
        Assert.Equal(
            "Parlez à Agent de la compagnie Asfog.",
            QuestStepSummary.OfStart(null, "l’Agent de la compagnie Asfog et Fils"));

    [Theory]
    // Relevé dans le catalogue : deux quêtes sur six cent quatre-vingt-treize
    // logent une phrase là où le site attend un nom.
    [InlineData("bateau pour vous rendre au village d’Albuera.")]
    [InlineData("clef secrète des crocs de verre")]
    public void Ce_qui_n_est_pas_un_nom_propre_ne_devient_pas_un_personnage(string brut) =>
        Assert.Null(QuestStepSummary.OfStart(null, brut));

    [Fact]
    public void Sans_lieu_ni_personne_le_depart_ne_dit_rien() =>
        Assert.Null(QuestStepSummary.OfStart(null, null));

    [Fact]
    public void Un_nom_ne_depasse_pas_six_mots()
    {
        // Sans plafond, une suite de mots capitalisés sans ponctuation ferait
        // un nom aussi long que la phrase.
        const string brut =
            "Une fois arrivé sur place, parlez à Yse Vewibad Lamarcheuse Deschemins "
            + "Latroisieme Duroyaume Deladouzieme Contree Lointaine";

        Assert.Equal(
            "Parlez à Yse Vewibad Lamarcheuse Deschemins Latroisieme Duroyaume.",
            QuestStepSummary.Of(brut));
    }

    [Fact]
    public void Un_resume_trop_long_est_coupe_a_un_mot_entier()
    {
        // Aucune amorce, aucune coordonnée : c'est la première phrase qui sert,
        // et elle dépasse le plafond.
        const string brut =
            "La quête consiste à parcourir toute la zone en ramassant les éclats de "
            + "cristal semés par la tempête, puis à les rapporter avant la tombée de la "
            + "nuit sous peine de devoir tout recommencer depuis le début.";

        var resume = QuestStepSummary.Of(brut);

        Assert.EndsWith("…", resume, StringComparison.Ordinal);

        // Le dernier morceau conservé est un mot entier du texte d'origine.
        var dernier = resume.TrimEnd('…').TrimEnd().Split(' ')[^1];

        Assert.Contains(dernier, brut, StringComparison.Ordinal);
    }
}

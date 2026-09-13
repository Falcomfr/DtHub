using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class QuestChainIndexTests
{
    private static QuestSummary Quete(
        string titre,
        string succes = "",
        int section = 1,
        params string[] prerequis) =>
        new()
        {
            Title = titre,
            Url = "https://papycha.fr/quete-" + titre.ToLowerInvariant().Replace(' ', '-') + "/",
            SuccessName = succes,
            SectionId = section,
            Prerequisites = prerequis,
        };

    /// <summary>
    /// The Albuera sequence, taken from the catalog: three quests outside
    /// any success line lead to the first quest of the "Médiation
    /// expéditive" success.
    /// </summary>
    private static readonly QuestSummary Debuter = Quete("Bien débuter");
    private static readonly QuestSummary Arrivee = Quete("Une arrivée mouvementée", prerequis: "Bien débuter");
    private static readonly QuestSummary Problemes =
        Quete("Le début des problèmes", "Médiation expéditive", 1, "Une arrivée mouvementée");

    private static readonly QuestChainIndex Albuera = new([Debuter, Arrivee, Problemes]);

    [Fact]
    public void Une_quete_sans_succes_mene_a_la_suivante()
    {
        Assert.Equal(Arrivee.Url, Albuera.NextOf(Debuter)?.Url);
        Assert.Equal(Problemes.Url, Albuera.NextOf(Arrivee)?.Url);
    }

    [Fact]
    public void La_chaine_traverse_l_entree_dans_un_succes()
    {
        // "Le début des problèmes" opens its success: the success list
        // does not give it a previous quest, but the prerequisite graph
        // does.
        Assert.Equal(Arrivee.Url, Albuera.PreviousOf(Problemes)?.Url);
    }

    [Fact]
    public void La_premiere_de_la_chaine_n_a_pas_de_precedente() =>
        Assert.Null(Albuera.PreviousOf(Debuter));

    [Fact]
    public void La_derniere_de_la_chaine_n_a_pas_de_suivante() =>
        Assert.Null(Albuera.NextOf(Problemes));

    [Fact]
    public void Une_suite_qui_se_ramifie_ne_donne_aucune_suivante()
    {
        // Observed: thirteen quests open several sequels. "Déchaînement
        // de spiritualité" opens four, and naming just one would be a
        // lie.
        var depart = Quete("Déchaînement de spiritualité");
        var index = new QuestChainIndex(
        [
            depart,
            Quete("L’Éternel Cerisier", prerequis: "Déchaînement de spiritualité"),
            Quete("La demoiselle du pont", prerequis: "Déchaînement de spiritualité"),
        ]);

        Assert.Null(index.NextOf(depart));
    }

    [Fact]
    public void Plusieurs_prerequis_ne_donnent_aucune_precedente()
    {
        var but = Quete("La belle ermite", prerequis: ["Bien débuter", "Une arrivée mouvementée"]);
        var index = new QuestChainIndex([Debuter, Arrivee, but]);

        Assert.Null(index.PreviousOf(but));
    }

    [Fact]
    public void Un_prerequis_qui_n_est_pas_une_quete_est_ignore()
    {
        // Out of five hundred sixty-seven distinct prerequisites, many
        // are items or conditions: "6 Chachas", "être niveau 50 minimum"
        // ("be at least level 50").
        var but = Quete("La belle ermite", prerequis: ["6 Chachas", "Bien débuter"]);
        var index = new QuestChainIndex([Debuter, but]);

        Assert.Equal(Debuter.Url, index.PreviousOf(but)?.Url);
    }

    [Fact]
    public void Le_rapprochement_se_moque_de_la_casse_et_des_apostrophes()
    {
        var cible = Quete("L’éveil de Pandala");
        var suite = Quete("Le Pandawa", prerequis: "L'EVEIL DE PANDALA");
        var index = new QuestChainIndex([cible, suite]);

        Assert.Equal(cible.Url, index.PreviousOf(suite)?.Url);
        Assert.Equal(suite.Url, index.NextOf(cible)?.Url);
    }

    // ------------------------------------------------------------------
    // A series' sequel does not always hang off its last quest.
    // ------------------------------------------------------------------

    private static QuestSummary Rang(string titre, string succes, int ordre, params string[] prerequis) =>
        Quete(titre, succes, 1, prerequis) with { PlayOrder = ordre };

    [Fact]
    public void La_serie_suivante_se_cherche_dans_tout_le_succes()
    {
        // Observed: "Médiation expéditive" continues from its fifth quest
        // out of six. The sixth therefore had no sequel at all.
        var cinq = Rang("Prochain arrêt : Astrub !", "Médiation expéditive", 5);
        var six = Rang("Le Kanojedo", "Médiation expéditive", 6, "Prochain arrêt : Astrub !");
        var apres = Rang("La découverte d’un destin", "Un nouveau départ", 1, "Prochain arrêt : Astrub !");

        var index = new QuestChainIndex([cinq, six, apres]);

        Assert.Equal(apres.Url, index.NextSeriesOf(six)?.Url);
    }

    [Fact]
    public void Deux_series_qui_partent_du_meme_succes_n_en_designent_aucune()
    {
        // Observed on two successes, including "Les survivants de
        // Frigost", which opens two of them.
        var une = Rang("À la recherche de Dan Lavy.", "Les survivants de Frigost", 6);
        var deux = Rang("Le dernier survivant", "Les survivants de Frigost", 7, "À la recherche de Dan Lavy.");
        var a = Rang("Inferno", "Ongles incarnés", 1, "À la recherche de Dan Lavy.");
        var b = Rang("Le forage", "Forage à tout va", 1, "À la recherche de Dan Lavy.");

        var index = new QuestChainIndex([une, deux, a, b]);

        Assert.Null(index.NextSeriesOf(deux));
    }

    [Fact]
    public void Une_quete_qui_n_ouvre_pas_son_succes_ne_compte_pas_pour_une_serie()
    {
        // Entering a series through its middle would make no sense: what
        // is being proposed is to start it.
        var fin = Rang("Le Kanojedo", "Médiation expéditive", 6);
        var premiere = Rang("La découverte d’un destin", "Un nouveau départ", 1);
        var seconde = Rang("La suite du destin", "Un nouveau départ", 2, "Le Kanojedo");

        var index = new QuestChainIndex([fin, premiere, seconde]);

        Assert.Null(index.NextSeriesOf(fin));
    }

    // ------------------------------------------------------------------
    // A prerequisite does not always name a quest.
    // ------------------------------------------------------------------

    /// <summary>
    /// The case observed on the site: "La légende du Chevalier de
    /// l'Automne" closes "Un nouveau départ", and the only thing that
    /// leads to the next success is the prerequisite "Succès Un nouveau
    /// départ réalisé" carried by "Dans les pas du Chevalier de
    /// l'Automne". Since that label is not the title of any quest, the
    /// edge did not enter the graph and the button stayed silent.
    /// </summary>
    [Fact]
    public void Un_prerequis_qui_nomme_un_succes_relie_les_deux_series()
    {
        var destin = Rang("La découverte d’un destin", "Un nouveau départ", 1);
        var devotion = Rang("Dévotion aux dieux", "Un nouveau départ", 2, "La découverte d’un destin");
        var legende = Rang(
            "La légende du chevalier de l’Automne", "Un nouveau départ", 3, "Dévotion aux dieux");
        var pas = Rang(
            "Dans les pas du Chevalier de l’Automne",
            "Devenir une légende",
            1,
            "Succès Un nouveau départ réalisé");

        var index = new QuestChainIndex([destin, devotion, legende, pas]);

        Assert.Equal(pas.Url, index.NextSeriesOf(legende)?.Url);

        // Requiring an entire success means requiring the quest that
        // closes it: the link therefore holds in both directions.
        Assert.Equal(pas.Url, index.NextOf(legende)?.Url);
        Assert.Equal(legende.Url, index.PreviousOf(pas)?.Url);
    }

    /// <summary>
    /// A milestone names the quest that sets it, not any other.
    /// </summary>
    [Fact]
    public void Un_prerequis_de_jalon_relie_la_quete_qui_le_pose()
    {
        var lac = Quete("L’essentiel est dans le Lac gelé");
        var suite = Quete("Les monologues du vaccin", prerequis: "L’essentiel est dans le Lac gelé atteint");

        var index = new QuestChainIndex([lac, suite]);

        Assert.Equal(suite.Url, index.NextOf(lac)?.Url);
        Assert.Equal(lac.Url, index.PreviousOf(suite)?.Url);
    }

    /// <summary>
    /// A success the catalog does not know links nothing: three of the
    /// successes cited as prerequisites exist nowhere else.
    /// </summary>
    [Fact]
    public void Un_succes_inconnu_ne_relie_rien()
    {
        var seule = Quete("Le forage", "Forage à tout va", 1, "Succès Route 1 réalisé");
        var index = new QuestChainIndex([seule]);

        Assert.Null(index.PreviousOf(seule));
    }

    [Fact]
    public void Une_quete_sans_succes_n_a_pas_de_serie_suivante() =>
        Assert.Null(new QuestChainIndex([Debuter]).NextSeriesOf(Debuter));

    [Fact]
    public void Une_quete_qui_se_nomme_elle_meme_ne_se_suit_pas()
    {
        var boucle = Quete("Lavomatique", prerequis: "Lavomatique");
        var index = new QuestChainIndex([boucle]);

        Assert.Null(index.NextOf(boucle));
        Assert.Null(index.PreviousOf(boucle));
    }
}

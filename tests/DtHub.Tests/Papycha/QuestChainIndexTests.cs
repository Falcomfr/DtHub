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
    /// La suite d'Albuera, relevée dans le catalogue : trois quêtes hors succès
    /// mènent à la première du succès « Médiation expéditive ».
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
        // « Le début des problèmes » ouvre son succès : la liste du succès ne
        // lui donne pas de précédente, le graphe des prérequis si.
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
        // Relevé : treize quêtes ouvrent plusieurs suites. « Déchaînement de
        // spiritualité » en ouvre quatre, et en désigner une mentirait.
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
        // Sur cinq cent soixante-sept prérequis distincts, beaucoup sont des
        // objets ou des conditions : « 6 Chachas », « être niveau 50 minimum ».
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
    // La suite d'une série ne pend pas toujours à sa dernière quête.
    // ------------------------------------------------------------------

    private static QuestSummary Rang(string titre, string succes, int ordre, params string[] prerequis) =>
        Quete(titre, succes, 1, prerequis) with { PlayOrder = ordre };

    [Fact]
    public void La_serie_suivante_se_cherche_dans_tout_le_succes()
    {
        // Relevé : « Médiation expéditive » se prolonge depuis sa cinquième
        // quête sur six. La sixième n'avait donc aucune suite.
        var cinq = Rang("Prochain arrêt : Astrub !", "Médiation expéditive", 5);
        var six = Rang("Le Kanojedo", "Médiation expéditive", 6, "Prochain arrêt : Astrub !");
        var apres = Rang("La découverte d’un destin", "Un nouveau départ", 1, "Prochain arrêt : Astrub !");

        var index = new QuestChainIndex([cinq, six, apres]);

        Assert.Equal(apres.Url, index.NextSeriesOf(six)?.Url);
    }

    [Fact]
    public void Deux_series_qui_partent_du_meme_succes_n_en_designent_aucune()
    {
        // Relevé sur deux succès, dont « Les survivants de Frigost », qui en
        // ouvre deux.
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
        // Entrer une série par son milieu n'aurait pas de sens : ce qu'on
        // propose, c'est de la commencer.
        var fin = Rang("Le Kanojedo", "Médiation expéditive", 6);
        var premiere = Rang("La découverte d’un destin", "Un nouveau départ", 1);
        var seconde = Rang("La suite du destin", "Un nouveau départ", 2, "Le Kanojedo");

        var index = new QuestChainIndex([fin, premiere, seconde]);

        Assert.Null(index.NextSeriesOf(fin));
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

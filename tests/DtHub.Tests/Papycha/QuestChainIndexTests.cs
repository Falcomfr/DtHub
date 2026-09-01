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

    [Fact]
    public void Une_quete_qui_se_nomme_elle_meme_ne_se_suit_pas()
    {
        var boucle = Quete("Lavomatique", prerequis: "Lavomatique");
        var index = new QuestChainIndex([boucle]);

        Assert.Null(index.NextOf(boucle));
        Assert.Null(index.PreviousOf(boucle));
    }
}

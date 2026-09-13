using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class QuestZonePlanTests
{
    private static QuestSummary Quete(
        string titre,
        string succes = "",
        int rang = 0,
        params string[] prerequis) =>
        new()
        {
            Title = titre,
            Url = "https://papycha.fr/" + titre.ToLowerInvariant().Replace(' ', '-') + "/",
            SuccessName = succes,
            PlayOrder = rang,
            Prerequisites = prerequis,
        };

    /// <summary>What the list would show, one block per line.</summary>
    private static string[] Lignes(IEnumerable<QuestZoneBlock> plan) =>
        [.. plan.Select(b => b.IsSuccess
            ? (b.IsContinuation ? "* " + b.SuccessName + " (suite)" : "* " + b.SuccessName)
            : "  " + b.Quests[0].Title)];

    /// <summary>
    /// A standalone quest that a success demands in the middle of its own
    /// chain slips in there, and the series resumes after it.
    ///
    /// Recorded at Amakna Castle: "Étre plus royaliste que le roi" ("Being
    /// more royalist than the king") requires nine standalone quests among its
    /// own quests. Treated as an unbreakable block, it used to push them all
    /// before or all after, and the list showed quests before what they
    /// require.
    /// </summary>
    [Fact]
    public void Une_quete_seule_se_glisse_entre_deux_quetes_d_un_succes()
    {
        List<QuestSummary> quetes =
        [
            Quete("Royaliste 1", "Etre royaliste", 1),
            Quete("Emissaire du roi", prerequis: "Royaliste 1"),
            Quete("Royaliste 2", "Etre royaliste", 2, "Emissaire du roi"),
        ];

        Assert.Equal(
            ["* Etre royaliste", "  Emissaire du roi", "* Etre royaliste (suite)"],
            Lignes(QuestZonePlan.Of(quetes, ["Etre royaliste"])));
    }

    /// <summary>
    /// Two successes never interleave, even when their prerequisites ask for
    /// it: at Frigost, eight successes require each other, and letting that
    /// happen produced a back-and-forth of fifteen subheadings between the
    /// same chains.
    /// </summary>
    [Fact]
    public void Deux_succes_ne_s_entrelacent_pas()
    {
        List<QuestSummary> quetes =
        [
            Quete("Docteur 1", "Jouer au docteur", 1),
            Quete("Docteur 2", "Jouer au docteur", 2, "Probleme 1"),
            Quete("Probleme 1", "Problemes et solutions", 1, "Docteur 1"),
        ];

        var plan = QuestZonePlan.Of(quetes, ["Jouer au docteur", "Problemes et solutions"]);

        Assert.Equal(["* Jouer au docteur", "* Problemes et solutions"], Lignes(plan));
        Assert.DoesNotContain(plan, b => b.IsContinuation);
    }

    /// <summary>
    /// The count on the first subheading stays that of the whole success: a
    /// chain cut in two does not become two.
    /// </summary>
    [Fact]
    public void Un_succes_coupe_garde_toutes_ses_quetes()
    {
        List<QuestSummary> quetes =
        [
            Quete("Royaliste 1", "Etre royaliste", 1),
            Quete("Emissaire du roi", prerequis: "Royaliste 1"),
            Quete("Royaliste 2", "Etre royaliste", 2, "Emissaire du roi"),
        ];

        var plan = QuestZonePlan.Of(quetes, ["Etre royaliste"]);

        Assert.Equal(2, plan.Where(b => b.IsSuccess).Sum(b => b.Quests.Count));
        Assert.Equal(3, plan.Sum(b => b.Quests.Count));
    }

    [Fact]
    public void Range_une_quete_seule_avant_le_succes_qui_la_reclame()
    {
        // Albuera: "Une arrivée mouvementée" ("An eventful arrival") opens
        // "Médiation expéditive" ("Swift mediation") without being part of it.
        List<QuestSummary> quetes =
        [
            Quete("Mediation 1", "Mediation expeditive", 1, "Une arrivee mouvementee"),
            Quete("Mediation 2", "Mediation expeditive", 2, "Mediation 1"),
            Quete("Une arrivee mouvementee"),
        ];

        Assert.Equal(
            ["  Une arrivee mouvementee", "* Mediation expeditive"],
            Lignes(QuestZonePlan.Of(quetes, ["Mediation expeditive"])));
    }

    [Fact]
    public void Range_une_quete_seule_apres_le_succes_dont_elle_decoule()
    {
        List<QuestSummary> quetes =
        [
            Quete("Emissaire du roi", prerequis: "Royaliste 2"),
            Quete("Royaliste 1", "Etre royaliste", 1),
            Quete("Royaliste 2", "Etre royaliste", 2, "Royaliste 1"),
        ];

        Assert.Equal(
            ["* Etre royaliste", "  Emissaire du roi"],
            Lignes(QuestZonePlan.Of(quetes, ["Etre royaliste"])));
    }

    /// <summary>
    /// The case recorded at Astrub: "La découverte d'un vaste monde" ("The
    /// discovery of a vast world") has only the success "Devenir une légende"
    /// ("Becoming a legend") as its prerequisite. It was filed after that
    /// success, but also after every other one, thirty ranks lower down: the
    /// sort placed it well after what it requires, so far that the progression
    /// could no longer be read.
    /// </summary>
    [Fact]
    public void Colle_une_quete_seule_au_succes_dont_elle_decoule()
    {
        List<QuestSummary> quetes =
        [
            Quete("Legende 1", "Devenir une legende", 1),
            Quete("Legende 2", "Devenir une legende", 2, "Legende 1"),
            Quete("La decouverte d’un vaste monde", prerequis: "Succes Devenir une legende realise"),
            Quete("Ville 1", "Quand on arrive en ville", 1),
            Quete("Piou 1", "Un Piou c’est tout", 1),
        ];

        Assert.Equal(
            [
                "* Devenir une legende",
                "  La decouverte d’un vaste monde",
                "* Quand on arrive en ville",
                "* Un Piou c’est tout",
            ],
            Lignes(QuestZonePlan.Of(
                quetes,
                ["Devenir une legende", "Quand on arrive en ville", "Un Piou c’est tout"])));
    }

    /// <summary>
    /// A chain of standalone quests hanging off a success follows it entirely,
    /// in the order it is chained.
    /// </summary>
    [Fact]
    public void Une_suite_de_quetes_seules_suit_le_succes_qui_l_ouvre()
    {
        List<QuestSummary> quetes =
        [
            Quete("Ailleurs 1", "Un autre succes", 1),
            Quete("Seule 2", prerequis: "Seule 1"),
            Quete("Seule 1", prerequis: "Ouvrir 1"),
            Quete("Ouvrir 1", "Ouvrir la voie", 1),
        ];

        Assert.Equal(
            ["* Ouvrir la voie", "  Seule 1", "  Seule 2", "* Un autre succes"],
            Lignes(QuestZonePlan.Of(quetes, ["Ouvrir la voie", "Un autre succes"])));
    }

    [Fact]
    public void Glisse_une_quete_seule_entre_deux_succes()
    {
        // Pandala: "En route pour Aerdala" ("On the way to Aerdala") follows
        // one success and opens the next.
        List<QuestSummary> quetes =
        [
            Quete("Choisir 1", "Choisir", 1),
            Quete("En route pour Aerdala", prerequis: "Choisir 1"),
            Quete("Vent 1", "Souffle le vent", 1, "En route pour Aerdala"),
        ];

        Assert.Equal(
            ["* Choisir", "  En route pour Aerdala", "* Souffle le vent"],
            Lignes(QuestZonePlan.Of(quetes, ["Choisir", "Souffle le vent"])));
    }

    [Fact]
    public void Enchaine_les_quetes_seules_entre_elles()
    {
        // The eighty alignment quests form a numbered series that alphabetical
        // order used to read as "1, 10, 11, 2".
        List<QuestSummary> quetes =
        [
            Quete("Bontarien 10", prerequis: "Bontarien 2"),
            Quete("Bontarien 1"),
            Quete("Bontarien 2", prerequis: "Bontarien 1"),
        ];

        Assert.Equal(
            ["  Bontarien 1", "  Bontarien 2", "  Bontarien 10"],
            Lignes(QuestZonePlan.Of(quetes, [])));
    }

    [Fact]
    public void Laisse_l_ordre_alphabetique_quand_aucun_prerequis_ne_se_reconnait()
    {
        // Astrub: twenty standalone quests, none of which names another quest.
        List<QuestSummary> quetes =
        [
            Quete("Devotion a Iop", prerequis: "Avoir le niveau 30"),
            Quete("Devotion a Cra"),
            Quete("Devotion a Feca"),
        ];

        Assert.Equal(
            ["  Devotion a Cra", "  Devotion a Feca", "  Devotion a Iop"],
            Lignes(QuestZonePlan.Of(quetes, [])));
    }

    [Fact]
    public void Ignore_un_prerequis_qui_designe_une_quete_absente_de_la_zone()
    {
        // The prerequisite exists, but in another zone: it cannot file
        // anything here, and the quest keeps its previous place.
        List<QuestSummary> quetes =
        [
            Quete("Ailleurs 1", "Un succes", 1),
            Quete("Venue d ailleurs", prerequis: "Une quete d une autre zone"),
        ];

        Assert.Equal(
            ["* Un succes", "  Venue d ailleurs"],
            Lignes(QuestZonePlan.Of(quetes, ["Un succes"])));
    }

    [Fact]
    public void Rend_un_ordre_total_quand_deux_succes_se_reclament_l_un_l_autre()
    {
        // A success is an unbreakable block: two successes that require each
        // other through different quests form a loop. It is settled by the
        // site's order.
        List<QuestSummary> quetes =
        [
            Quete("B 1", "Second", 1, "A 1"),
            Quete("A 1", "Premier", 1),
            Quete("A 2", "Premier", 2, "B 1"),
        ];

        Assert.Equal(
            ["* Premier", "* Second"],
            Lignes(QuestZonePlan.Of(quetes, ["Premier", "Second"])));
    }

    [Fact]
    public void Suit_l_ordre_du_site_entre_succes_que_rien_ne_lie()
    {
        List<QuestSummary> quetes =
        [
            Quete("Z 1", "Zulu", 1),
            Quete("A 1", "Alpha", 1),
        ];

        Assert.Equal(
            ["* Zulu", "* Alpha"],
            Lignes(QuestZonePlan.Of(quetes, ["Zulu", "Alpha"])));
    }

    [Fact]
    public void Range_un_succes_sans_rang_apres_les_autres_mais_avant_les_quetes_seules()
    {
        List<QuestSummary> quetes =
        [
            Quete("Seule"),
            Quete("Inconnu 1", "Succes inconnu du site", 1),
            Quete("Connu 1", "Succes connu", 1),
        ];

        Assert.Equal(
            ["* Succes connu", "* Succes inconnu du site", "  Seule"],
            Lignes(QuestZonePlan.Of(quetes, ["Succes connu"])));
    }

    [Fact]
    public void Range_les_quetes_d_un_succes_dans_l_ordre_de_jeu()
    {
        List<QuestSummary> quetes =
        [
            Quete("Derniere", "Un succes", 2),
            Quete("Premiere", "Un succes", 1),
        ];

        var bloc = Assert.Single(QuestZonePlan.Of(quetes, ["Un succes"]));

        Assert.Equal(["Premiere", "Derniere"], bloc.Quests.Select(q => q.Title));
    }

    [Fact]
    public void Renvoie_en_fin_de_liste_une_quete_que_rien_ne_lie()
    {
        // The site says nothing about its place. Leaving it in the sort put it
        // at random, between two successes, for lack of anything better to
        // output at that point.
        List<QuestSummary> quetes =
        [
            Quete("On recherche Ali Grothor"),
            Quete("Premier 1", "Premier succes", 1),
            Quete("Second 1", "Second succes", 1, "Premier 1"),
        ];

        Assert.Equal(
            ["* Premier succes", "* Second succes", "  On recherche Ali Grothor"],
            Lignes(QuestZonePlan.Of(quetes, ["Premier succes", "Second succes"])));
    }

    [Fact]
    public void Garde_dans_le_fil_une_quete_seule_qu_un_lien_rattache()
    {
        // One link is enough, in either direction: this is what sets it apart
        // from a quest that nothing places.
        List<QuestSummary> quetes =
        [
            Quete("Rien ne la lie"),
            Quete("Elle ouvre le succes"),
            Quete("Succes 1", "Un succes", 1, "Elle ouvre le succes"),
        ];

        Assert.Equal(
            ["  Elle ouvre le succes", "* Un succes", "  Rien ne la lie"],
            Lignes(QuestZonePlan.Of(quetes, ["Un succes"])));
    }

    [Fact]
    public void Range_par_titre_les_quetes_que_rien_ne_lie()
    {
        List<QuestSummary> quetes =
        [
            Quete("Zoulou"),
            Quete("Alpha 1", "Un succes", 1),
            Quete("Bravo"),
        ];

        Assert.Equal(
            ["* Un succes", "  Bravo", "  Zoulou"],
            Lignes(QuestZonePlan.Of(quetes, ["Un succes"])));
    }

    [Fact]
    public void Rend_une_liste_vide_pour_une_zone_vide()
    {
        Assert.Empty(QuestZonePlan.Of([], ["Un succes"]));
    }

    [Fact]
    public void Ignore_une_quete_qui_se_nomme_elle_meme_en_prerequis()
    {
        List<QuestSummary> quetes = [Quete("Boucle", prerequis: "Boucle")];

        Assert.Equal(["  Boucle"], Lignes(QuestZonePlan.Of(quetes, [])));
    }
}

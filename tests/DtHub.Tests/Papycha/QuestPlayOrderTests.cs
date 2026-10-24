using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The order of quests in an achievement. Nothing tested it, and the
/// rank map contradicted it thirteen times.
/// </summary>
public sealed class QuestPlayOrderTests
{
    private static QuestSummary Quete(
        string titre,
        int ordre = 0,
        int chaine = 0,
        string succes = "Un succès",
        params string[] prerequis) =>
        new()
        {
            Title = titre,
            Url = "https://papycha.fr/quete-" + titre.ToLowerInvariant().Replace(' ', '-') + "/",
            SuccessName = succes,
            SectionId = 1,
            PlayOrder = ordre,
            ChainStep = chaine,
            Prerequisites = prerequis,
        };

    private static IReadOnlyList<string> Titres(IEnumerable<QuestSummary> quests) =>
        [.. quests.Select(q => q.Title)];

    [Fact]
    public void Sans_prerequis_l_ordre_est_celui_de_la_carte()
    {
        var plan = QuestPlayOrder.Sorted([Quete("C", 3), Quete("A", 1), Quete("B", 2)]);

        Assert.Equal(["A", "B", "C"], Titres(plan));
    }

    /// <summary>
    /// A zero does not mean "first" but "unknown": it goes to the back
    /// of the queue, otherwise the quest would pass itself off as the
    /// achievement's opener.
    /// </summary>
    [Fact]
    public void Un_rang_inconnu_passe_en_queue()
    {
        var plan = QuestPlayOrder.Sorted([Quete("Sans rang"), Quete("A", 1), Quete("B", 2)]);

        Assert.Equal(["A", "B", "Sans rang"], Titres(plan));
    }

    [Fact]
    public void A_rang_egal_le_rang_de_chaine_departage()
    {
        var plan = QuestPlayOrder.Sorted([Quete("Tard", chaine: 9), Quete("Tôt", chaine: 2)]);

        Assert.Equal(["Tôt", "Tard"], Titres(plan));
    }

    /// <summary>
    /// The case found on the site: in "Un Piou, c'est tout !", "L'île
    /// Céleste" carries rank 2 and requires "Le voyage vers Incarnam",
    /// which carries rank 3. The list therefore showed the quest
    /// before what it requires.
    /// </summary>
    [Fact]
    public void Un_prerequis_l_emporte_sur_la_carte_des_rangs()
    {
        var plan = QuestPlayOrder.Sorted(
        [
            Quete("Origine Inpiounnue", 1),
            Quete("L’île Céleste", 2, prerequis: "Le voyage vers Incarnam"),
            Quete("Le voyage vers Incarnam", 3),
        ]);

        Assert.Equal(["Origine Inpiounnue", "Le voyage vers Incarnam", "L’île Céleste"], Titres(plan));
    }

    /// <summary>
    /// A quest that requires its own achievement requires it in full:
    /// it therefore comes after everything else, whatever its rank.
    /// </summary>
    [Fact]
    public void Une_quete_qui_reclame_son_succes_passe_en_dernier()
    {
        var plan = QuestPlayOrder.Sorted(
        [
            Quete("Première", 1),
            Quete("Plantala", 2, prerequis: "Succès Un succès réalisé"),
            Quete("Troisième", 3),
        ]);

        Assert.Equal(["Première", "Troisième", "Plantala"], Titres(plan));
    }

    /// <summary>
    /// A prerequisite from another achievement does not order anything
    /// here.
    /// </summary>
    [Fact]
    public void Un_prerequis_venu_d_ailleurs_ne_change_rien()
    {
        var plan = QuestPlayOrder.Sorted(
        [
            Quete("A", 1, prerequis: "Succès Un autre succès réalisé"),
            Quete("B", 2, prerequis: "Une quête d’une autre zone"),
        ]);

        Assert.Equal(["A", "B"], Titres(plan));
    }

    /// <summary>
    /// Two quests that each require the other cannot be resolved:
    /// rather than a truncated list, we fall back to the rank map.
    /// </summary>
    [Fact]
    public void Une_boucle_retombe_sur_la_carte_sans_rien_perdre()
    {
        var plan = QuestPlayOrder.Sorted(
        [
            Quete("A", 1, prerequis: "B"),
            Quete("B", 2, prerequis: "A"),
        ]);

        Assert.Equal(["A", "B"], Titres(plan));
    }

    [Fact]
    public void Une_liste_vide_ou_d_un_seul_element_se_rend_telle_quelle()
    {
        Assert.Empty(QuestPlayOrder.Sorted([]));
        Assert.Single(QuestPlayOrder.Sorted([Quete("Seule")]));
    }

    [Fact]
    public void La_place_d_une_quete_est_son_rang_dans_l_ordre_de_jeu_sur_la_taille_de_sa_serie()
    {
        var b = Quete("B", 2) with { Id = 2 };
        var catalogue = new[]
        {
            Quete("C", 3) with { Id = 3 }, Quete("A", 1) with { Id = 1 }, b,
            Quete("Seule", succes: "Autre") with { Id = 4 }, Quete("Hors", succes: "") with { Id = 5 },
        };

        Assert.Equal((2, 3), QuestPlayOrder.Position(catalogue, b));
        // Seule de son succès, ou sans succès : pas de place à donner.
        Assert.Null(QuestPlayOrder.Position(catalogue, catalogue[3]));
        Assert.Null(QuestPlayOrder.Position(catalogue, catalogue[4]));
    }

    /// <summary>
    /// « Agriculture ou alchimie » en petit : R puis deux voies jouées en
    /// zigzag, A1-A2-A3 ou B1-B2-B3, la n-ième de chaque voie alternative de
    /// l'autre.
    /// </summary>
    internal static QuestSummary[] DeuxVoies() =>
    [
        Quete("R", 1) with { Id = 1 },
        Quete("A1", 2, prerequis: "R") with { Id = 11, Alternatives = [21] },
        Quete("B1", 3, prerequis: "R") with { Id = 21, Alternatives = [11] },
        Quete("A2", 4, prerequis: "A1") with { Id = 12, Alternatives = [22] },
        Quete("B2", 5, prerequis: "B1") with { Id = 22, Alternatives = [12] },
        Quete("A3", 6, prerequis: "A2") with { Id = 13, Alternatives = [23] },
        Quete("B3", 7, prerequis: "B2") with { Id = 23, Alternatives = [13] },
    ];

    [Fact]
    public void Dans_une_serie_a_deux_voies_la_place_se_compte_le_long_de_la_voie()
    {
        var catalogue = DeuxVoies();

        Assert.Equal((3, 4), QuestPlayOrder.Position(catalogue, catalogue[4]));
        Assert.Equal(["R", "A1", "A2", "A3"], Titres(QuestPlayOrder.Way(catalogue, [catalogue[3], catalogue[5]])));
        // Avant l'embranchement, la voie n'est pas choisie : toute la série.
        Assert.Equal((1, 7), QuestPlayOrder.Position(catalogue, catalogue[0]));
    }
}

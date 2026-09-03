using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// L'ordre des quêtes d'un succès. Rien ne l'éprouvait, et la carte des rangs
/// le contredisait treize fois.
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
    /// Un zéro ne dit pas « premier » mais « on ne sait pas » : il passe en
    /// queue, sans quoi la quête se donnerait pour l'ouverture du succès.
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
    /// Le cas relevé sur le site : dans « Un Piou, c'est tout ! », « L'île
    /// Céleste » porte le rang 2 et exige « Le voyage vers Incarnam », qui
    /// porte le rang 3. La liste montrait donc la quête avant ce qu'elle exige.
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
    /// Une quête qui réclame son propre succès le réclame en entier : elle
    /// passe donc après tout le reste, quel que soit son rang.
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

    /// <summary>Un prérequis d'un autre succès ne range rien ici.</summary>
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
    /// Deux quêtes qui se réclament l'une l'autre ne peuvent pas être
    /// départagées : plutôt qu'une liste tronquée, on retombe sur la carte.
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
}

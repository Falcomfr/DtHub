using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The choice of a quest's neighbours. It used to live in the view,
/// which the tests do not reach, and nothing covered it.
/// </summary>
public sealed class QuestNeighbourhoodTests
{
    private static QuestSummary Quete(
        string titre,
        string succes = "",
        int ordre = 0,
        params string[] prerequis) =>
        new()
        {
            Title = titre,
            Url = "https://papycha.fr/quete-" + titre.ToLowerInvariant().Replace(' ', '-') + "/",
            SuccessName = succes,
            SectionId = 1,
            PlayOrder = ordre,
            Prerequisites = prerequis,
        };

    private static readonly QuestSummary Une = Quete("Première", "Un nouveau départ", 1);
    private static readonly QuestSummary Deux = Quete("Deuxième", "Un nouveau départ", 2);
    private static readonly QuestSummary Trois = Quete("Troisième", "Un nouveau départ", 3);

    private static readonly QuestSummary[] Succes = [Une, Deux, Trois];

    [Fact]
    public void Au_milieu_du_succes_les_deux_voisines_sont_celles_de_la_liste()
    {
        var voisines = QuestNeighbourhood.Of(Deux, Succes, chain: null);

        Assert.Equal(Une.Url, voisines.Previous?.Url);
        Assert.Equal(Trois.Url, voisines.Next?.Url);
        Assert.Equal(2, voisines.Rank);
        Assert.Equal(3, voisines.Count);
    }

    [Fact]
    public void La_premiere_du_succes_n_a_pas_de_precedente()
    {
        var voisines = QuestNeighbourhood.Of(Une, Succes, chain: null);

        Assert.Null(voisines.Previous);
        Assert.Equal(Deux.Url, voisines.Next?.Url);
        Assert.Equal(1, voisines.Rank);
    }

    [Fact]
    public void La_derniere_du_succes_n_a_pas_de_suivante_sans_le_graphe()
    {
        var voisines = QuestNeighbourhood.Of(Trois, Succes, chain: null);

        Assert.Equal(Deux.Url, voisines.Previous?.Url);
        Assert.Null(voisines.Next);
        Assert.Equal(3, voisines.Rank);
    }

    /// <summary>
    /// Where the list stops, the graph takes over. This is the reported
    /// case: the last quest of a success leads to the first quest of
    /// the next one.
    /// </summary>
    [Fact]
    public void Le_graphe_prolonge_la_liste_au_dela_du_succes()
    {
        var apres = Quete("Dans les pas", "Devenir une légende", 1, "Succès Un nouveau départ réalisé");
        QuestSummary[] tout = [Une, Deux, Trois, apres];

        var voisines = QuestNeighbourhood.Of(Trois, tout, new QuestChainIndex(tout));

        Assert.Equal(apres.Url, voisines.Next?.Url);
    }

    /// <summary>
    /// The success's list comes before the graph: it is the order being
    /// walked, and the graph only fills in its gaps.
    /// </summary>
    [Fact]
    public void La_liste_du_succes_l_emporte_sur_le_graphe()
    {
        var ailleurs = Quete("Ailleurs", "Autre succès", 1, "Deuxième");
        QuestSummary[] tout = [Une, Deux, Trois, ailleurs];

        var voisines = QuestNeighbourhood.Of(Deux, tout, new QuestChainIndex(tout));

        Assert.Equal(Trois.Url, voisines.Next?.Url);
    }

    [Fact]
    public void Une_quete_hors_succes_n_a_ni_rang_ni_voisines_de_liste()
    {
        var seule = Quete("Solitaire");

        var voisines = QuestNeighbourhood.Of(seule, [seule, .. Succes], chain: null);

        Assert.Null(voisines.Previous);
        Assert.Null(voisines.Next);
        Assert.Equal(0, voisines.Rank);
        Assert.Equal(0, voisines.Count);
    }

    /// <summary>
    /// An unknown rank is worth zero and sorts to the back, not the
    /// front: otherwise a quest with an unknown rank would pass for the
    /// first one of its success.
    /// </summary>
    [Fact]
    public void Un_rang_inconnu_se_range_en_queue()
    {
        var sansRang = Quete("Sans rang", "Un nouveau départ");
        QuestSummary[] tout = [Une, Deux, Trois, sansRang];

        var voisines = QuestNeighbourhood.Of(Trois, tout, chain: null);

        Assert.Equal(sansRang.Url, voisines.Next?.Url);
        Assert.Equal(3, voisines.Rank);
        Assert.Equal(4, voisines.Count);
    }

    [Fact]
    public void Sans_quete_il_n_y_a_rien_a_dire()
    {
        var voisines = QuestNeighbourhood.Of(quest: null, Succes, chain: null);

        Assert.Null(voisines.Previous);
        Assert.Null(voisines.Next);
    }

    /// <summary>
    /// The case observed on the site, with its real titles and real
    /// ordering. "Manque de moule" requires "Titi Gobelait", but comes
    /// after "Un avenir de krotte de Trooll", which requires nothing.
    /// </summary>
    private static readonly QuestSummary Titi =
        Quete("Titi Gobelait le magobelin", "Le théâtre des gobelins", 4);

    private static readonly QuestSummary Krotte =
        Quete("Un avenir de krotte de Trooll", "Le théâtre des gobelins", 5);

    private static readonly QuestSummary Moule =
        Quete("Manque de moule", "Le théâtre des gobelins", 6, "Titi Gobelait le magobelin");

    private static readonly QuestSummary[] Gobelins = [Titi, Krotte, Moule];

    [Fact]
    public void La_liste_du_succes_ne_saute_pas_une_quete_sans_prerequis()
    {
        // Following the prerequisite would lead from Titi to Manque de
        // moule, skipping Un avenir de krotte de Trooll. The list, on
        // the other hand, takes them in order.
        var voisines = QuestNeighbourhood.Of(Titi, Gobelins, new QuestChainIndex(Gobelins));

        Assert.Equal(Krotte.Title, voisines.Next?.Title);
        Assert.Equal(1, voisines.Rank);
        Assert.Equal(3, voisines.Count);
    }

    [Fact]
    public void Au_milieu_de_la_liste_c_est_elle_qui_tranche()
    {
        // What the view reads to know whether the site's column is
        // allowed to correct it: in the middle, no.
        var voisines = QuestNeighbourhood.Of(Krotte, Gobelins, new QuestChainIndex(Gobelins));

        Assert.True(voisines.NextFromList);
        Assert.True(voisines.PreviousFromList);
    }

    [Fact]
    public void Aux_bornes_du_succes_la_liste_laisse_la_place()
    {
        var premiere = QuestNeighbourhood.Of(Titi, Gobelins, new QuestChainIndex(Gobelins));
        var derniere = QuestNeighbourhood.Of(Moule, Gobelins, new QuestChainIndex(Gobelins));

        // The first one has no previous quest in the list, the last one
        // no next quest: this is where, and only where, the site fills
        // in the gap.
        Assert.False(premiere.PreviousFromList);
        Assert.True(premiere.NextFromList);

        Assert.True(derniere.PreviousFromList);
        Assert.False(derniere.NextFromList);
    }

    [Fact]
    public void Hors_de_tout_succes_la_liste_ne_dit_rien()
    {
        var seule = Quete("Une quête isolée");

        var voisines = QuestNeighbourhood.Of(seule, [seule], new QuestChainIndex([seule]));

        Assert.False(voisines.NextFromList);
        Assert.False(voisines.PreviousFromList);
    }
}

using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// Le choix des voisines d'une quête. Il vivait dans la vue, que les épreuves
/// n'atteignent pas, et rien ne le couvrait.
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
    /// Là où la liste s'arrête, le graphe prend le relais. C'est le cas signalé :
    /// la dernière quête d'un succès mène à la première du suivant.
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
    /// La liste du succès passe avant le graphe : elle est l'ordre qu'on
    /// parcourt, et le graphe ne comble que ses trous.
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
    /// Un rang inconnu vaut zéro et se range en queue, non en tête : sans quoi
    /// une quête de rang inconnu passerait pour la première de son succès.
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
}

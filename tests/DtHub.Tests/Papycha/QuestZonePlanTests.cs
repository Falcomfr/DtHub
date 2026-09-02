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

    /// <summary>Ce que la liste montrerait, un bloc par ligne.</summary>
    private static string[] Lignes(IEnumerable<QuestZoneBlock> plan) =>
        [.. plan.Select(b => b.IsSuccess
            ? "* " + b.SuccessName
            : "  " + b.Quests[0].Title)];

    [Fact]
    public void Range_une_quete_seule_avant_le_succes_qui_la_reclame()
    {
        // Albuera : « Une arrivée mouvementée » ouvre « Médiation expéditive »
        // sans en faire partie.
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

    [Fact]
    public void Glisse_une_quete_seule_entre_deux_succes()
    {
        // Pandala : « En route pour Aerdala » suit un succès et ouvre le suivant.
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
        // Les quatre-vingts quêtes d'alignement forment une suite numérotée que
        // l'ordre alphabétique lisait « 1, 10, 11, 2 ».
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
        // Astrub : vingt quêtes seules dont aucune ne nomme une autre quête.
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
        // Le prérequis existe, mais dans une autre zone : il ne peut rien ranger
        // ici, et la quête garde sa place d'avant.
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
        // Un succès est un bloc insécable : deux succès qui se réclament par des
        // quêtes différentes forment une boucle. On tranche par l'ordre du site.
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

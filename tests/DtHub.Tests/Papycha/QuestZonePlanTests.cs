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
            ? (b.IsContinuation ? "* " + b.SuccessName + " (suite)" : "* " + b.SuccessName)
            : "  " + b.Quests[0].Title)];

    /// <summary>
    /// Une quête seule que le succès réclame au milieu de sa propre suite s'y
    /// glisse, et la série reprend après elle.
    ///
    /// Relevé au Château d'Amakna : « Étre plus royaliste que le roi » réclame
    /// neuf quêtes seules entre ses quêtes. Traité comme un bloc insécable, il
    /// les rejetait toutes avant ou toutes après, et la liste montrait des
    /// quêtes avant ce qu'elles exigent.
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
    /// Deux succès ne s'entrelacent jamais, même quand leurs prérequis le
    /// demandent : à Frigost, huit succès se réclament mutuellement, et les
    /// laisser faire donnait un va-et-vient de quinze intertitres entre les
    /// mêmes séries.
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
    /// Le compte du premier intertitre reste celui du succès entier : une série
    /// coupée en deux n'en devient pas deux.
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

    /// <summary>
    /// Le cas relevé à Astrub : « La découverte d'un vaste monde » n'a pour
    /// prérequis que le succès « Devenir une légende ». Elle était rangée après
    /// ce succès, mais aussi après tous les autres, trente rangs plus bas :
    /// le tri la plaçait bien après ce qu'elle exige, si loin que la
    /// progression ne se lisait plus.
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
    /// Une suite de quêtes seules qui pend à un succès le suit tout entière,
    /// dans l'ordre où on l'enchaîne.
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
    public void Renvoie_en_fin_de_liste_une_quete_que_rien_ne_lie()
    {
        // Le site ne dit rien de sa place. La laisser dans le tri la mettait au
        // hasard, entre deux succès, faute de mieux à sortir à ce moment-là.
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
        // Un lien suffit, dans un sens ou dans l'autre : c'est ce qui la sépare
        // d'une quête que rien ne place.
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

using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class TouchLocalizationTests
{
    private static QuestSummary Quest(int id, string title, int level = 0, string success = "", int step = 0, params string[] prerequisites) => new()
    {
        Id = id,
        Url = $"https://example.test/{id}",
        Title = title,
        SearchKey = QuestSearch.Normalize(title),
        Level = level,
        SuccessName = success,
        ChainStep = step,
        Prerequisites = prerequisites,
    };

    private static QuestCatalogDocument Catalog() => new()
    {
        Quests =
        [
            Quest(1, "Quête alpha", 151, "Cité dor", 3),
            Quest(2, "Quête bêta", 0, "", 0, "Quête alpha"),
            Quest(3, "Quête gamma"),
        ],
        Dungeons =
        [
            new DungeonSummary { Id = 5, Title = "Donjon fr", SearchKey = "donjon fr", Key = "Clé fr" },
            new DungeonSummary { Id = 6, Title = "Boss fr", SearchKey = "boss fr", Key = "Clé boss" },
        ],
        SuccessOrder = ["Cité dor", "Autre"],
    };

    private static readonly TouchIdTable Table = new()
    {
        Quests = new() { [1] = [10], [2] = [11, 12, 13], [3] = [99] },
        Dungeons = new() { [5] = new([50, 51]), [6] = new([], 7) },
        Series = new() { ["Cité dor"] = 90 },
    };

    private static TouchLanguageNames Names(int min = 120, int max = 120) => new()
    {
        Quests = new()
        {
            [10] = new("Invented EN", min, max, []),
            [11] = new("Twin one", 15, 200, []),
            [12] = new("Twin one", 1, 1, []),
            [13] = new("Twin two", 1, 1, []),
        },
        Dungeons = new() { [50] = new("Dark Lair", 500), [51] = new("Bright Arena", 501) },
        Monsters = new() { [7] = "Boss en" },
        Items = new() { [500] = "Key en" },
        Achievements = new() { [90] = "Golden City", [91] = "Linked Series" },
    };

    private static QuestSummary Q(QuestCatalogDocument d, int id) => d.Quests.Single(q => q.Id == id);

    [Fact]
    public void En_anglais_le_titre_et_la_recherche_prennent_le_nom_du_jeu()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table, Names(), translateNames: true);

        Assert.Equal("Invented EN", Q(doc, 1).Title);
        Assert.Single(QuestSearch.Filter(doc.Quests, "invented"), q => q.Id == 1);
        Assert.Single(QuestSearch.Filter(doc.Quests, "alpha"), q => q.Id == 1);
    }

    [Fact]
    public void Une_page_groupee_joint_les_noms_distincts_par_une_barre()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table, Names(), translateNames: true);

        Assert.Equal("Twin one / Twin two", Q(doc, 2).Title);
    }

    [Fact]
    public void Les_prerequis_suivent_les_titres()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table, Names(), translateNames: true);
        var b = Q(doc, 2);

        Assert.Equal(1, new QuestChainIndex(doc.Quests).Find(b.Prerequisites[0])!.Id);
    }

    [Fact]
    public void En_francais_les_titres_restent_ceux_de_Papycha_mais_les_series_se_corrigent()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table, Names(), translateNames: false);

        Assert.Equal("Quête alpha", Q(doc, 1).Title);
        Assert.Equal("Golden City", Q(doc, 1).SuccessName);
        Assert.Equal(["Golden City", "Autre"], doc.SuccessOrder);
        Assert.Equal("Quête alpha", Q(doc, 2).Prerequisites[0]);
    }

    [Fact]
    public void En_francais_un_prerequis_de_quete_n_est_pas_reecrit()
    {
        var source = Catalog();
        source.Quests[1] = Quest(2, "Quête bêta", 0, "", 0, "Quete ALPHA");

        var doc = TouchLocalization.Apply(source, Table, Names(), translateNames: false);

        Assert.Equal("Quete ALPHA", Q(doc, 2).Prerequisites[0]);
    }

    [Fact]
    public void Un_nom_du_jeu_partage_par_deux_titres_francais_differents_laisse_les_titres_francais()
    {
        var table = Table with { Quests = new() { [1] = [10], [2] = [11], [3] = [14] } };
        var names = Names();
        names.Quests[14] = new("invented en", 30, 30, []);

        var doc = TouchLocalization.Apply(Catalog(), table, names, translateNames: true);

        Assert.Equal("Quête alpha", Q(doc, 1).Title);
        Assert.Equal("Quête gamma", Q(doc, 3).Title);
        Assert.Equal(30, Q(doc, 3).Level);
        Assert.Equal("Twin one", Q(doc, 2).Title);
        Assert.Equal(1, new QuestChainIndex(doc.Quests).Find(Q(doc, 2).Prerequisites[0])!.Id);
    }

    [Fact]
    public void Une_page_groupee_ne_repete_pas_un_nom_a_la_casse_ou_l_accent_pres()
    {
        var names = Names();
        names.Quests[12] = new("Twín ONE", 1, 1, []);

        var doc = TouchLocalization.Apply(Catalog(), Table, names, translateNames: true);

        Assert.Equal("Twin one / Twin two", Q(doc, 2).Title);
    }

    [Fact]
    public void Le_niveau_du_jeu_ne_sert_que_s_il_est_precis()
    {
        var precise = TouchLocalization.Apply(Catalog(), Table, Names(120, 120), true);
        var vague = TouchLocalization.Apply(Catalog(), Table, Names(15, 200), true);

        Assert.Equal(120, Q(precise, 1).Level);
        Assert.Equal(151, Q(vague, 1).Level);
    }

    [Fact]
    public void Un_lien_du_jeu_range_la_quete_apres_celles_de_Papycha()
    {
        var source = Catalog();
        source.Quests.Add(Quest(4, "Quête delta", 0, "Linked Series", 5));
        var table = Table with { Links = new() { [3] = 91 } };

        var doc = TouchLocalization.Apply(source, table, Names(), true);

        Assert.Equal("Linked Series", Q(doc, 3).SuccessName);
        Assert.Equal(6, Q(doc, 3).ChainStep);
        Assert.Contains("Linked Series", doc.SuccessOrder);
        Assert.Equal("Golden City", Q(doc, 1).SuccessName);
    }

    [Fact]
    public void Une_quete_inconnue_de_la_table_reste_en_francais()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table, Names(), translateNames: true);

        Assert.Equal("Quête gamma", Q(doc, 3).Title);
        Assert.Equal(QuestSearch.Normalize("Quête gamma"), Q(doc, 3).SearchKey);
    }

    [Fact]
    public void Un_donjon_prend_les_noms_du_jeu_et_sa_cle()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table, Names(), translateNames: true);
        var lair = doc.Dungeons.Single(d => d.Id == 5);
        var boss = doc.Dungeons.Single(d => d.Id == 6);

        Assert.Equal("Dark Lair / Bright Arena", lair.Title);
        Assert.Equal("Key en", lair.Key);
        Assert.Equal("Boss en", boss.Title);
        Assert.Equal("Clé boss", boss.Key);
    }

    [Fact]
    public void Le_document_source_n_est_pas_modifie()
    {
        var source = Catalog();

        _ = TouchLocalization.Apply(source, Table, Names(), translateNames: true);

        Assert.Equal("Quête alpha", Q(source, 1).Title);
        Assert.Equal("Cité dor", Q(source, 1).SuccessName);
        Assert.Equal("Donjon fr", source.Dungeons[0].Title);
        Assert.Equal(["Cité dor", "Autre"], source.SuccessOrder);
    }

    private static QuestCatalogDocument WithSuccessPrerequisite()
    {
        var source = Catalog();
        source.Quests[2] = Quest(3, "Quête gamma", 0, "", 0, "Succès Cité dor réalisé");
        return source;
    }

    [Theory]
    [InlineData(true, "Golden City")]
    [InlineData(false, "Golden City")]
    public void Un_prerequis_de_succes_suit_le_renommage_de_la_serie(bool translateNames, string expected)
    {
        var doc = TouchLocalization.Apply(WithSuccessPrerequisite(), Table, Names(), translateNames);
        var gamma = Q(doc, 3);

        Assert.Equal($"Succès {expected} réalisé", gamma.Prerequisites[0]);
        Assert.Equal(1, new QuestChainIndex(doc.Quests).PreviousOf(gamma)!.Id);
    }

    [Fact]
    public void Un_prerequis_atteint_garde_sa_forme_avec_le_titre_traduit()
    {
        var source = Catalog();
        source.Quests[2] = Quest(3, "Quête gamma", 0, "", 0, "[FIN] Quête alpha atteint");

        var doc = TouchLocalization.Apply(source, Table, Names(), translateNames: true);

        Assert.Equal("[FIN] Invented EN atteint", Q(doc, 3).Prerequisites[0]);
    }

    [Fact]
    public void Une_serie_absente_de_la_table_garde_son_nom()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table with { Series = [] }, Names(), true);

        Assert.Equal("Cité dor", Q(doc, 1).SuccessName);
    }

    [Fact]
    public void Un_lien_ne_remplace_pas_une_serie_deja_connue()
    {
        var doc = TouchLocalization.Apply(Catalog(), Table with { Links = new() { [1] = 91 } }, Names(), true);

        Assert.Equal("Golden City", Q(doc, 1).SuccessName);
        Assert.Equal(3, Q(doc, 1).ChainStep);
    }
}

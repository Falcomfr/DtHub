using DtHub.Core.Papycha;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Papycha;

public class QuestCatalogServiceTests
{
    private static (QuestCatalogService Service, FakePapychaClient Client, InMemoryDocumentStore<QuestCatalogDocument> Store) Build(
        FakePapychaClient? client = null)
    {
        var fake = client ?? new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithQuest(2, "Complètement givré", 135)
            .WithSection(18, "Astrub")
            .WithSection(135, "Île de Frigost");

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();

        return (new QuestCatalogService(fake, store), fake, store);
    }

    [Fact]
    public async Task Le_premier_appel_indexe_et_range_le_catalogue()
    {
        var (service, client, store) = Build();
        using var _ = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(2, catalog.Quests.Count);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, store.Writes);
        Assert.NotNull(catalog.IndexedUtc);
    }

    [Fact]
    public async Task Un_catalogue_frais_n_est_pas_reindexe()
    {
        var (service, client, _) = Build();
        using var _2 = service;

        await service.GetAsync(cancellationToken: CancellationToken.None);
        await service.GetAsync(cancellationToken: CancellationToken.None);

        // Asking the site again for eight pages every time the window opens
        // would be as rude as it is pointless.
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Une_panne_de_reseau_laisse_le_catalogue_precedent_utilisable()
    {
        var (service, client, _) = Build();
        using var _2 = service;

        await service.GetAsync(cancellationToken: CancellationToken.None);

        client.Failure = new HttpRequestException("le site ne répond pas");

        var catalog = await service.RefreshAsync(cancellationToken: CancellationToken.None);

        // Searching in yesterday's list beats not being able to search at all.
        Assert.Equal(2, catalog.Quests.Count);
        Assert.NotNull(service.LastFailure);
    }

    [Fact]
    public async Task Un_site_qui_ne_rend_rien_n_ecrase_pas_le_cache()
    {
        var (service, _, store) = Build();
        using var _2 = service;

        await service.GetAsync(cancellationToken: CancellationToken.None);

        var vide = new FakePapychaClient();
        var second = new QuestCatalogService(vide, store);
        using var _3 = second;

        var catalog = await second.RefreshAsync(cancellationToken: CancellationToken.None);

        // An empty response can mean the site is under maintenance: we keep
        // what we have.
        Assert.Equal(2, catalog.Quests.Count);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task La_recherche_ignore_les_accents_et_les_apostrophes()
    {
        var (service, _, _) = Build();
        using var _2 = service;

        await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Single(service.Search("completement givre"));
        Assert.Single(service.Search("dragon astrub"));
        Assert.Single(service.Search("ASTRUB DRAGON"));
        Assert.Empty(service.Search("bouftou"));
    }

    [Fact]
    public async Task Une_rubrique_rend_ses_quetes()
    {
        var (service, _, _) = Build();
        using var _2 = service;

        await service.GetAsync(cancellationToken: CancellationToken.None);

        var frigost = service.InSection(135);

        Assert.Equal("Complètement givré", Assert.Single(frigost).Title);
    }

    [Fact]
    public async Task Un_catalogue_perime_est_reindexe()
    {
        var (service, client, _) = Build();
        using var _2 = service;

        await service.GetAsync(cancellationToken: CancellationToken.None);

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        await store.SaveAsync(
            new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
                Quests = [new QuestSummary { Id = 9, Title = "Vieille entrée" }],
            },
            CancellationToken.None);

        var vieilli = new QuestCatalogService(client, store) { Freshness = TimeSpan.FromDays(7) };
        using var _3 = vieilli;

        var catalog = await vieilli.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(2, catalog.Quests.Count);
    }

    /// <summary>
    /// A catalog read two hours ago, with the fingerprint the site carried at
    /// that time. Two hours because the sentinel only bothers the site beyond
    /// one hour.
    /// </summary>
    private static async Task<(QuestCatalogService Service, FakePapychaClient Client)> Veille(
        SiteStamp connue,
        SiteStamp? annoncee)
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithSection(18, "Astrub");

        client.Stamp = annoncee;

        // The sentinel queries only the categories we read, not the whole
        // site: a correction on an article outside our categories must no
        // longer trigger anything.
        if (annoncee is not null)
        {
            client.CategoryStamps.Add(new CategoryStamp(7, annoncee.Modified, annoncee.Posts));
        }

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();

        await store.SaveAsync(
            new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
                SiteModifiedUtc = connue.Modified,
                SitePosts = connue.Posts,
                SiteCategories = [new CategoryStamp(7, connue.Modified, connue.Posts)],
                Quests = [new QuestSummary { Id = 9, Title = "Vieille entrée" }],
            },
            CancellationToken.None);

        return (new QuestCatalogService(client, store), client);
    }

    private static SiteStamp Empreinte(int posts = 1012, int joursAvant = 3) =>
        new(DateTimeOffset.UtcNow - TimeSpan.FromDays(joursAvant), posts);

    [Fact]
    public async Task Un_site_qui_n_a_pas_bouge_ne_provoque_aucune_relecture()
    {
        var connue = Empreinte();
        var (service, client) = await Veille(connue, connue);
        using var pareil = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, client.StampCalls);
        Assert.Equal(0, client.Calls);
        Assert.Equal("Vieille entrée", Assert.Single(catalog.Quests).Title);
    }

    [Fact]
    public async Task Un_article_de_plus_provoque_une_relecture()
    {
        var connue = Empreinte();
        var (service, client) = await Veille(connue, connue with { Posts = connue.Posts + 1 });
        using var deplus = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, client.Calls);
        Assert.Equal("Le dragon d'Astrub", Assert.Single(catalog.Quests).Title);
    }

    [Fact]
    public async Task Un_article_modifie_depuis_provoque_une_relecture()
    {
        var connue = Empreinte();
        var (service, client) = await Veille(
            connue, connue with { Modified = DateTimeOffset.UtcNow });
        using var modifie = service;

        _ = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Un_site_qui_ne_repond_pas_ne_provoque_pas_de_relecture()
    {
        // Keeping what we have beats throwing away a catalog for lack of
        // network.
        var (service, client) = await Veille(Empreinte(), annoncee: null);
        using var mort = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(0, client.Calls);
        Assert.Equal("Vieille entrée", Assert.Single(catalog.Quests).Title);
    }

    [Fact]
    public async Task Un_catalogue_lu_il_y_a_une_minute_ne_derange_meme_pas_le_site()
    {
        // Opening and closing the window ten times within the hour must not
        // produce ten requests.
        var client = new FakePapychaClient().WithQuest(1, "Le dragon d'Astrub", 18);
        var store = new InMemoryDocumentStore<QuestCatalogDocument>();

        await store.SaveAsync(
            new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1),
                SiteModifiedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(3),
                SitePosts = 1012,
                SiteCategories = [new CategoryStamp(7, DateTimeOffset.UtcNow - TimeSpan.FromDays(3), 782)],
                Quests = [new QuestSummary { Id = 9, Title = "Vieille entrée" }],
            },
            CancellationToken.None);

        using var service = new QuestCatalogService(client, store);

        _ = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(0, client.StampCalls);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Une_relecture_retient_l_empreinte_du_site()
    {
        var (service, client, _) = Build();
        using var _2 = service;

        client.Stamp = Empreinte(posts: 1013);
        client.CategoryStamps.Add(new CategoryStamp(7, client.Stamp.Modified, 782));

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1013, catalog.SitePosts);
        Assert.Equal(client.Stamp.Modified, catalog.SiteModifiedUtc);
        Assert.Equal(782, Assert.Single(catalog.SiteCategories).Posts);
    }

    [Fact]
    public async Task Un_article_hors_de_nos_categories_ne_provoque_rien()
    {
        // The site has one thousand twelve articles, we only read nine hundred
        // of them. A correction on the others used to bump the whole site's
        // date and cost fifty seconds of needless rereading.
        var connue = Empreinte();
        var (service, client) = await Veille(connue, connue);
        using var ailleurs = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, client.StampCalls);
        Assert.Equal(0, client.Calls);
        Assert.Equal("Vieille entrée", Assert.Single(catalog.Quests).Title);
    }

    [Fact]
    public async Task L_indexation_recopie_le_nom_des_rubriques_dans_les_quetes()
    {
        var (service, _, _) = Build();
        using var _2 = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        var givre = catalog.Quests.Single(q => q.Title == "Complètement givré");

        // The zone carries the quest, but quest search no longer uses it for
        // that: searching "frigost" used to return one hundred seventy-seven
        // unwanted results. The zone group is what addresses that.
        Assert.Contains(135, givre.SectionIds);
        Assert.Empty(service.Search("frigost"));

        Assert.Single(service.SearchAll("frigost").Zones);
    }

    [Fact]
    public async Task Une_quete_n_est_rangee_que_sous_une_rubrique()
    {
        // A quest from Astrub also carries "Amakna". Listing it under both
        // would make it count twice and appear twice.
        var client = new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 25, 18)
            .WithSection(18, "Astrub", count: 56)
            .WithSection(25, "Amakna", count: 300);

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(18, catalog.Quests[0].SectionId);
        Assert.Single(service.InSection(18));
        Assert.Empty(service.InSection(25));
    }

    [Fact]
    public async Task Une_page_du_site_reclame_les_quetes_qu_aucune_categorie_ne_range()
    {
        // Measured on the site: 150 out of 782 quests have no category and
        // were only reachable through search.
        var client = new FakePapychaClient()
            .WithQuest(1, "Une quête sans rubrique")
            .WithPage("Quêtes du Krosmoz", "https://papycha.fr/quetes-du-krosmoz/", 1);

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        var section = Assert.Single(catalog.Sections);

        Assert.Equal("Quêtes du Krosmoz", section.Name);
        Assert.Equal(1, section.Count);
        Assert.Single(service.InSection(section.Id));
    }

    [Fact]
    public async Task Une_page_qui_designe_une_categorie_ne_cree_pas_de_rubrique_jumelle()
    {
        // "Quêtes de Frigost" ("Frigost Quests") and "Île de Frigost" ("Isle
        // of Frigost") name the same place. Offering both would be a
        // duplicate; so the page's quests join the category instead.
        var client = new FakePapychaClient()
            .WithQuest(1, "Complètement givré", 135)
            .WithQuest(2, "Une quête de Frigost sans rubrique")
            .WithSection(135, "Île de Frigost", count: 184)
            .WithPage("Quêtes de Frigost", "https://papycha.fr/quetes-de-frigost/", 1, 2);

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        var section = Assert.Single(catalog.Sections);

        Assert.Equal("Île de Frigost", section.Name);
        Assert.Equal(2, section.Count);
    }

    [Fact]
    public async Task Une_quete_que_personne_ne_reclame_reste_atteignable()
    {
        var client = new FakePapychaClient().WithQuest(1, "Une orpheline");

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        var section = Assert.Single(catalog.Sections);

        Assert.Equal(QuestCatalogService.OtherSectionId, section.Id);
        Assert.Equal("Autres quêtes", section.Name);
        Assert.Single(service.InSection(QuestCatalogService.OtherSectionId));
    }

    [Fact]
    public async Task Les_rubriques_suivent_l_ordre_du_site()
    {
        // The site publishes its ranking in the table of its "Quêtes"
        // ("Quests") page. The number of quests plays no part in it: Astrub
        // comes before Frigost there, even though Frigost has far more.
        var client = new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithQuest(2, "Complètement givré", 135)
            .WithQuest(3, "Encore du givre", 135)
            .WithSection(18, "Astrub", count: 56)
            .WithSection(135, "Île de Frigost", count: 184)
            .WithPage("Quêtes d'Astrub", "https://papycha.fr/quetes-dastrub/")
            .WithPage("Quêtes de Frigost", "https://papycha.fr/quetes-de-frigost/");

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(["Astrub", "Île de Frigost"], catalog.Sections.Select(s => s.Name));
    }

    [Fact]
    public async Task Une_quete_nommee_par_deux_pages_figure_dans_les_deux()
    {
        // Recorded from the site: seventeen of the eighteen Time Bubbles
        // quests also appear on the Krosmoz page, which includes them. Keeping
        // only one left the other nearly empty, even though the site really
        // does list them in both places.
        var client = new FakePapychaClient()
            .WithQuest(1, "Une quête sans rubrique")
            .WithQuest(2, "Une autre sans rubrique")
            .WithPage("Quêtes du Krosmoz", "https://papycha.fr/quetes-du-krosmoz/", 1, 2)
            .WithPage("Quêtes des Bulles Temporelles", "https://papycha.fr/bulles/", 1);

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        var bulles = Assert.Single(catalog.Sections, s => s.Name == "Quêtes des Bulles Temporelles");
        var krosmoz = Assert.Single(catalog.Sections, s => s.Name == "Quêtes du Krosmoz");

        Assert.Equal(1, bulles.Count);
        Assert.Equal(2, krosmoz.Count);

        // The section that places the quest in a search is the smallest one.
        Assert.Equal(bulles.Id, catalog.Quests[0].SectionId);
    }

    [Fact]
    public async Task Chaque_quete_porte_le_succes_que_sa_page_de_rubrique_lui_donne()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Rencontre du ratième type", 18)
            .WithQuest(2, "L'Astrub d'en bas", 18)
            .WithQuest(3, "Une quête sans succès", 18)
            .WithSection(18, "Astrub", count: 56)
            .WithPage("Quêtes d'Astrub", "https://papycha.fr/quetes-dastrub/")
            .WithSuccess("Quand on arrive en ville", 1, 2);

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal("Quand on arrive en ville", catalog.Quests[0].SuccessName);
        Assert.Equal("Quand on arrive en ville", catalog.Quests[1].SuccessName);

        // Three hundred seventy-three quests out of seven hundred eighty-two
        // carry one: for those that do not, none is invented.
        Assert.Equal(string.Empty, catalog.Quests[2].SuccessName);
    }

    [Fact]
    public async Task Un_succes_entame_par_une_rubrique_y_figure_en_entier()
    {
        // Recorded from the site: "Se mettre au ver" (literally "get down to
        // the worm") counts four quests, three filed under Amakna and one
        // under the main quests. The main quests section used to show this
        // success with only one quest.
        var client = new FakePapychaClient()
            .WithQuest(1, "Brêche Mais Intense", 25)
            .WithQuest(2, "Le ver de trop", 25)
            .WithQuest(3, "Le ver itay sort de la bouche des enfers", 25)
            .WithQuest(4, "Sauter le pas du trépas")
            .WithSection(25, "Amakna", count: 30)
            .WithPage("Quêtes principales", "https://papycha.fr/quetes-principales/", 4)
            .WithPage("Quêtes d'Amakna", "https://papycha.fr/quetes-damakna/")
            .WithSuccess("Se mettre au ver", 1, 2, 3, 4);

        var (service, _, _) = Build(client);
        await service.GetAsync(cancellationToken: CancellationToken.None);

        // Only three quests carried the Amakna category, but a success plays
        // as one whole: the section that claims one of them claims them all.
        var amakna = service.InSection(25);

        Assert.Equal(4, amakna.Count);
        Assert.All(amakna, q => Assert.Equal("Se mettre au ver", q.SuccessName));

        // And the main quests page keeps its own, whole as well.
        var principales = Assert.Single(
            service.Catalog.Sections, s => s.Name == "Quêtes principales");

        Assert.Equal(4, service.InSection(principales.Id).Count);
    }

    [Fact]
    public async Task Un_succes_est_entier_dans_chaque_rubrique_qui_le_reclame()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Une de Frigost", 135)
            .WithQuest(2, "Une autre de Frigost", 135)
            .WithQuest(3, "Une d'Astrub", 18)
            .WithSection(18, "Astrub", count: 56)
            .WithSection(135, "Île de Frigost", count: 184)
            .WithPage("Quêtes d'Astrub", "https://papycha.fr/quetes-dastrub/")
            .WithPage("Quêtes de Frigost", "https://papycha.fr/quetes-de-frigost/")
            .WithSuccess("Les survivants de Frigost", 1, 2, 3);

        var (service, _, _) = Build(client);
        await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(3, service.InSection(135).Count);
        Assert.Equal(3, service.InSection(18).Count);
    }

    [Fact]
    public async Task La_carte_embarquee_complete_les_intertitres()
    {
        // Page subheadings attach 380 quests, the embedded map 505: it is
        // drawn from the intro block of each quest, which the pages do not all
        // cover.
        var client = new FakePapychaClient()
            .WithQuest(1, "Une quête coiffée par un intertitre")
            .WithQuest(2, "Une quête qu'aucun intertitre ne coiffe")
            .WithPage("Quêtes principales", "https://papycha.fr/quetes-principales/", 1, 2)
            .WithSuccess("Vu par la page", 1);

        var seed = new FakeQuestSuccessSeed().With(2, "Vu par la carte");

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal("Vu par la page", catalog.Quests[0].SuccessName);
        Assert.Equal("Vu par la carte", catalog.Quests[1].SuccessName);
    }

    [Fact]
    public async Task La_carte_embarquee_l_emporte_sur_un_intertitre()
    {
        // The intro block is what the quest says about itself; a subheading is
        // an editorial filing choice. The two sources even write "à la racine"
        // ("at the root") and "par la racine" ("by the root") for the same
        // success.
        var client = new FakePapychaClient()
            .WithQuest(1, "La main occulte")
            .WithPage("Quêtes d'Astrub", "https://papycha.fr/quetes-dastrub/", 1)
            .WithSuccess("Brûler le pissenlit à la racine", 1);

        var seed = new FakeQuestSuccessSeed()
            .With(1, "Brûler le pissenlit par la racine");

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal("Brûler le pissenlit par la racine", catalog.Quests[0].SuccessName);
    }

    [Fact]
    public async Task Sans_carte_embarquee_les_intertitres_suffisent()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "La main occulte")
            .WithPage("Quêtes d'Astrub", "https://papycha.fr/quetes-dastrub/", 1)
            .WithSuccess("Brûler le pissenlit à la racine", 1);

        var (service, _, _) = Build(client);
        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal("Brûler le pissenlit à la racine", catalog.Quests[0].SuccessName);
    }

    [Fact]
    public async Task La_place_dans_la_chaine_suit_la_quete()
    {
        // It is used to present a success's quests in the order they are
        // played: "De la caillasse plein les poches" ("Pockets full of
        // gravel") goes from step 1 to step 6, whereas alphabetical order is
        // not one at all.
        var client = new FakePapychaClient()
            .WithQuest(1, "Nettoyage express")
            .WithQuest(2, "Langage corporel")
            .WithPage("Quêtes répétables", "https://papycha.fr/quetes-repetables/", 1, 2);

        var seed = new FakeQuestSuccessSeed()
            .With(1, "De la caillasse plein les poches", chainStep: 6)
            .With(2, "De la caillasse plein les poches", chainStep: 1);

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(6, catalog.Quests[0].ChainStep);
        Assert.Equal(1, catalog.Quests[1].ChainStep);
    }

    [Fact]
    public async Task La_place_dans_le_succes_suit_la_quete()
    {
        // It comes from the prerequisites the site publishes: "Les rescapés de
        // Frigost" ("The survivors of Frigost") requires "[FIN] L'essentiel
        // est dans le Lac gelé" ("[END] The essential is in the frozen lake"),
        // so the latter comes first, which no other field on the site states.
        var client = new FakePapychaClient()
            .WithQuest(1, "Les rescapés de Frigost")
            .WithQuest(2, "L'essentiel est dans le Lac gelé")
            .WithPage("Quêtes de Frigost", "https://papycha.fr/quetes-de-frigost/", 1, 2);

        var seed = new FakeQuestSuccessSeed()
            .With(1, "La maire dénie", chainStep: 2, playOrder: 3)
            .With(2, "La maire dénie", chainStep: 2, playOrder: 2);

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(3, catalog.Quests[0].PlayOrder);
        Assert.Equal(2, catalog.Quests[1].PlayOrder);
    }

    [Fact]
    public async Task Le_rang_d_un_succes_se_prend_sur_ses_quetes_et_non_sur_l_intitule()
    {
        // The site does not write the same name in the two places it names it:
        // "Fri Carré" in the subheading, "Fri carré" in the quest. The second
        // is authoritative everywhere else, and matching the two by their text
        // lost the rank.
        var client = new FakePapychaClient()
            .WithQuest(1, "Une pêche d'enfer")
            .WithPage("Quêtes principales", "https://papycha.fr/quetes-principales/")
            .WithSuccess("Fri Carré", 1);

        var seed = new FakeQuestSuccessSeed().With(1, "Fri carré");

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        using var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(["Fri carré"], catalog.SuccessOrder);
        Assert.Equal("Fri carré", catalog.Quests[0].SuccessName);
    }

    [Fact]
    public async Task Un_intertitre_en_gras_range_meme_sans_la_marque_du_site()
    {
        // Three successes have no subheading other than their bare name.
        // Without them they fell to the end of the zone, in alphabetical
        // order.
        var client = new FakePapychaClient()
            .WithQuest(1, "Le retour des morts pas vraiment vivants")
            .WithQuest(2, "Cwoque ma Cawotte")
            .WithPage("Île des Wabbits", "https://papycha.fr/quete-de-lile-des-wabbits/")
            .WithHeading("Les morts", 1)
            .WithHeading("La cawotte", 2);

        var seed = new FakeQuestSuccessSeed()
            .With(1, "Le retour des morts pas vraiment vivants")
            .With(2, "Cwoque ma Cawotte");

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        using var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(
            ["Le retour des morts pas vraiment vivants", "Cwoque ma Cawotte"],
            catalog.SuccessOrder);
    }

    [Fact]
    public async Task Un_intertitre_dont_aucune_quete_ne_porte_de_succes_ne_range_rien()
    {
        // "À la chasse aux Goroku" ("Hunting Goroku") only points to pages the
        // catalog ignores: keeping it would put a name in the rank list that
        // no quest carries.
        var client = new FakePapychaClient()
            .WithQuest(1, "Une pêche d'enfer")
            .WithPage("Quêtes principales", "https://papycha.fr/quetes-principales/")
            .WithSuccess("À la chasse aux Goroku", 7)
            .WithSuccess("Fri carré", 1);

        var seed = new FakeQuestSuccessSeed().With(1, "Fri carré");

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        using var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(["Fri carré"], catalog.SuccessOrder);
    }

    [Fact]
    public async Task L_ordre_des_succes_suit_celui_des_pages()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Une pêche d'enfer")
            .WithQuest(2, "Le dragon d'Astrub")
            .WithPage("Île de Frigost", "https://papycha.fr/quetes-de-frigost/")
            .WithSuccess("Second", 1)
            .WithPage("Astrub", "https://papycha.fr/quetes-dastrub/")
            .WithSuccess("Premier", 2);

        var seed = new FakeQuestSuccessSeed().With(1, "Second").With(2, "Premier");

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        using var service = new QuestCatalogService(client, store, seed);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        // The rank is the site's, not the alphabet's: "Second" really does
        // come first because its page does.
        Assert.Equal(["Second", "Premier"], catalog.SuccessOrder);
    }
}

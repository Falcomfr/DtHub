using System.Globalization;

using DtHub.Core.Localization;
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

        return (new QuestCatalogService(fake, store, fake), fake, store);
    }

    private sealed class OneQuestTable : ITouchIdTableSource
    {
        public TouchIdTable Load() => new() { Quests = new() { [1] = [10] } };
    }

    [Fact]
    public async Task Le_catalogue_servi_porte_les_noms_du_jeu_le_catalogue_sauve_reste_francais()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));

        try
        {
            var (_, client, store) = Build();
            using var touch = new TouchNameService(new FakeDofusTouchClient(), new InMemoryDocumentStore<TouchNamesDocument>(), new OneQuestTable());
            using var service = new QuestCatalogService(client, store, client, touch);

            await service.GetAsync(cancellationToken: CancellationToken.None);
            Assert.Equal("Le dragon d'Astrub", service.Catalog.Quests.Single(q => q.Id == 1).Title);

            await service.RefreshNamesAsync();

            Assert.Equal("nom 10 en", service.Catalog.Quests.Single(q => q.Id == 1).Title);
            Assert.Equal("Le dragon d'Astrub", service.Source.Quests.Single(q => q.Id == 1).Title);
            Assert.Equal("Le dragon d'Astrub", (await store.LoadAsync(CancellationToken.None)).Quests.Single(q => q.Id == 1).Title);
        }
        finally
        {
            Strings.Speak(null);
        }
    }

    [Fact]
    public async Task Sans_noms_en_cache_le_catalogue_reste_celui_de_Papycha()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("en"));

        try
        {
            var (_, client, store) = Build();
            using var touch = new TouchNameService(
                new FakeDofusTouchClient { Failure = new HttpRequestException("jeu injoignable") },
                new InMemoryDocumentStore<TouchNamesDocument>(),
                new OneQuestTable());
            using var service = new QuestCatalogService(client, store, client, touch);

            var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);
            await service.RefreshNamesAsync();

            Assert.Equal("Le dragon d'Astrub", catalog.Quests.Single(q => q.Id == 1).Title);
            Assert.Equal("Le dragon d'Astrub", service.Catalog.Quests.Single(q => q.Id == 1).Title);
            Assert.NotNull(touch.LastFailure);
        }
        finally
        {
            Strings.Speak(null);
        }
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
    public async Task Une_lecture_reussie_efface_la_panne_precedente()
    {
        var (service, client, _) = Build();
        using var _2 = service;

        client.Failure = new HttpRequestException("le site ne répond pas");
        await service.RefreshAsync(cancellationToken: CancellationToken.None);

        client.Failure = null;
        await service.RefreshAsync(cancellationToken: CancellationToken.None);

        // The window would otherwise keep saying the site is down.
        Assert.Null(service.LastFailure);
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

        // The site no longer lists the old entry: it stays, the merge never
        // removes anything.
        Assert.Equal(3, catalog.Quests.Count);
        Assert.Contains(catalog.Quests, q => q.Title == "Vieille entrée");
    }

    /// <summary>
    /// A catalog read two hours ago, with the date the API announced at that
    /// time. Two hours because the sentinel only bothers the site beyond a
    /// quarter of an hour.
    /// </summary>
    private static async Task<(QuestCatalogService Service, FakePapychaClient Client)> Veille(
        SiteStamp connue,
        SiteStamp? annoncee)
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithSection(18, "Astrub");

        client.Stamp = annoncee;

        var store = new InMemoryDocumentStore<QuestCatalogDocument>();

        await store.SaveAsync(
            new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
                SiteModifiedUtc = connue.Modified,
                Quests = [new QuestSummary { Id = 9, Title = "Vieille entrée" }],
            },
            CancellationToken.None);

        return (new QuestCatalogService(client, store, client), client);
    }

    private static SiteStamp Empreinte(int joursAvant = 3) =>
        new(DateTimeOffset.UtcNow - TimeSpan.FromDays(joursAvant));

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
    public async Task Un_article_modifie_depuis_provoque_une_relecture()
    {
        var connue = Empreinte();
        var (service, client) = await Veille(connue, new SiteStamp(DateTimeOffset.UtcNow));
        using var modifie = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, client.Calls);
        Assert.Equal(["Vieille entrée", "Le dragon d'Astrub"], catalog.Quests.Select(q => q.Title));
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
                Quests = [new QuestSummary { Id = 9, Title = "Vieille entrée" }],
            },
            CancellationToken.None);

        using var service = new QuestCatalogService(client, store, client);

        _ = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(0, client.StampCalls);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Une_relecture_retient_la_date_annoncee_par_l_api()
    {
        var (service, client, _) = Build();
        using var _2 = service;

        client.Stamp = Empreinte();

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(client.Stamp.Modified, catalog.SiteModifiedUtc);
    }

    [Fact]
    public async Task Sans_cache_le_catalogue_embarque_sert_de_base_et_est_fusionne()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithSection(18, "Astrub");
        client.KnownQuests.Add(new QuestSummary { Id = 2, Title = "Dévotion à Crâ", SuccessName = "Dévotion", SectionIds = [18] });
        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        using var service = new QuestCatalogService(client, store, client);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, client.SnapshotLoads);
        Assert.Equal(["Dévotion à Crâ", "Le dragon d'Astrub"], catalog.Quests.Select(q => q.Title));
        Assert.Equal(2, Assert.Single(catalog.Sections).Count);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task Sans_cache_et_sans_reseau_le_catalogue_embarque_est_rendu()
    {
        var client = new FakePapychaClient().WithSection(18, "Astrub");
        client.KnownQuests.Add(new QuestSummary { Id = 2, Title = "Dévotion à Crâ", SectionIds = [18] });
        client.Failure = new HttpRequestException("le site ne répond pas");
        var store = new InMemoryDocumentStore<QuestCatalogDocument>();
        using var service = new QuestCatalogService(client, store, client);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        // A new player without network still gets the whole list.
        Assert.Equal("Dévotion à Crâ", Assert.Single(catalog.Quests).Title);
        Assert.NotNull(service.LastFailure);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Un_cache_existant_est_fusionne_une_fois_puis_laisse_tranquille()
    {
        // A player who updates DT Hub has a catalog read through the closed
        // API, dated from that API. The first opening merges once; the next
        // ones, within a quarter of an hour, do not even ask.
        var client = new FakePapychaClient().WithQuest(1, "Le dragon d'Astrub", 18).WithSection(18, "Astrub");
        client.Stamp = Empreinte(joursAvant: 1);
        var store = new InMemoryDocumentStore<QuestCatalogDocument>();

        await store.SaveAsync(
            new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(6),
                SiteModifiedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(10),
                Quests = [new QuestSummary { Id = 9, Title = "Vieille entrée" }],
                Sections = [new QuestSection { Id = 18, Name = "Astrub" }],
            },
            CancellationToken.None);

        using var service = new QuestCatalogService(client, store, client);

        var first = await service.GetAsync(cancellationToken: CancellationToken.None);
        var calls = (client.Calls, client.StampCalls);
        _ = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(2, first.Quests.Count);
        Assert.Equal(0, client.SnapshotLoads);
        Assert.Equal(1, client.Calls);
        Assert.Equal(calls, (client.Calls, client.StampCalls));
    }

    [Fact]
    public async Task Un_cache_d_un_ancien_schema_cede_la_place_au_catalogue_embarque()
    {
        var client = new FakePapychaClient().WithSection(18, "Astrub");
        client.KnownQuests.Add(new QuestSummary { Id = 2, Title = "Embarquée", SectionIds = [18] });
        client.Failure = new HttpRequestException("le site ne répond pas");
        var store = new InMemoryDocumentStore<QuestCatalogDocument>();

        await store.SaveAsync(
            new QuestCatalogDocument
            {
                SchemaVersion = 3,
                IndexedUtc = DateTimeOffset.UtcNow,
                Quests = [new QuestSummary { Id = 9, Title = "Illisible" }],
            },
            CancellationToken.None);

        using var service = new QuestCatalogService(client, store, client);

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal("Embarquée", Assert.Single(catalog.Quests).Title);
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
}

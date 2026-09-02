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

        // Aller redemander huit pages au site à chaque ouverture de la fenêtre
        // serait grossier autant qu'inutile.
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

        // Chercher dans une liste d'hier vaut mieux que ne rien pouvoir chercher.
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

        // Une réponse vide peut être un site en maintenance : on garde.
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
    /// Un catalogue lu il y a deux heures, avec l'empreinte que le site portait
    /// alors. Deux heures parce que la sentinelle ne dérange le site qu'au-delà
    /// d'une heure.
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
                SitePosts = connue.Posts,
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
        // Garder ce qu'on a vaut mieux que jeter un catalogue faute de réseau.
        var (service, client) = await Veille(Empreinte(), annoncee: null);
        using var mort = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(0, client.Calls);
        Assert.Equal("Vieille entrée", Assert.Single(catalog.Quests).Title);
    }

    [Fact]
    public async Task Un_catalogue_lu_il_y_a_une_minute_ne_derange_meme_pas_le_site()
    {
        // Ouvrir et refermer la fenêtre dix fois dans l'heure ne doit pas
        // produire dix demandes.
        var client = new FakePapychaClient().WithQuest(1, "Le dragon d'Astrub", 18);
        var store = new InMemoryDocumentStore<QuestCatalogDocument>();

        await store.SaveAsync(
            new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1),
                SiteModifiedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(3),
                SitePosts = 1012,
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

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1013, catalog.SitePosts);
        Assert.Equal(client.Stamp.Modified, catalog.SiteModifiedUtc);
    }

    [Fact]
    public async Task L_indexation_recopie_le_nom_des_rubriques_dans_les_quetes()
    {
        var (service, _, _) = Build();
        using var _2 = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        var givre = catalog.Quests.Single(q => q.Title == "Complètement givré");

        // La zone porte la quête, mais la recherche de quêtes ne s'en sert plus :
        // chercher « frigost » rendait cent soixante-dix-sept résultats dont on
        // n'avait pas voulu. C'est le groupe des zones qui répond à cela.
        Assert.Contains(135, givre.SectionIds);
        Assert.Empty(service.Search("frigost"));

        Assert.Single(service.SearchAll("frigost").Zones);
    }

    [Fact]
    public async Task Une_quete_n_est_rangee_que_sous_une_rubrique()
    {
        // Une quête d'Astrub porte aussi « Amakna ». La lister sous les deux la
        // ferait compter deux fois et apparaître deux fois.
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
        // Mesuré sur le site : 150 quêtes sur 782 n'ont aucune catégorie et
        // n'étaient atteignables que par la recherche.
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
        // « Quêtes de Frigost » et « Île de Frigost » désignent le même endroit.
        // Les offrir toutes deux serait un doublon ; les quêtes de la page
        // rejoignent donc la catégorie.
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
        // Le site publie son classement dans le tableau de sa page « Quêtes ».
        // Le nombre de quêtes n'y a aucune part : Astrub y précède Frigost, qui
        // en compte pourtant bien davantage.
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
        // Relevé sur le site : dix-sept des dix-huit quêtes des Bulles
        // Temporelles figurent aussi sur la page du Krosmoz, qui les englobe.
        // N'en retenir qu'une laissait l'autre presque vide, alors que le site
        // les range bel et bien aux deux endroits.
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

        // La rubrique qui situe la quête dans une recherche est la plus petite.
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

        // Trois cent soixante-treize quêtes sur sept cent quatre-vingt-deux en
        // portent un : à celles qui n'en ont pas, on n'en invente pas.
        Assert.Equal(string.Empty, catalog.Quests[2].SuccessName);
    }

    [Fact]
    public async Task Un_succes_entame_par_une_rubrique_y_figure_en_entier()
    {
        // Relevé sur le site : « Se mettre au ver » compte quatre quêtes, trois
        // rangées sous Amakna et une sous les quêtes principales. La rubrique
        // des principales affichait donc ce succès avec une seule quête.
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

        // Trois quêtes seulement portaient la catégorie Amakna, mais un succès
        // se joue d'un tenant : la rubrique qui en réclame une les réclame
        // toutes.
        var amakna = service.InSection(25);

        Assert.Equal(4, amakna.Count);
        Assert.All(amakna, q => Assert.Equal("Se mettre au ver", q.SuccessName));

        // Et la page des principales garde les siennes, entières elle aussi.
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
        // Les intertitres des pages rattachent 380 quêtes, la carte 505 : elle
        // est tirée du bloc d'intro de chaque quête, que les pages ne coiffent
        // pas toutes.
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
        // Le bloc d'intro est ce que la quête dit d'elle-même ; un intertitre
        // est un rangement éditorial. Les deux sources écrivent d'ailleurs
        // « à la racine » et « par la racine » pour le même succès.
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
        // Elle sert à présenter les quêtes d'un succès dans l'ordre où l'on y
        // joue : « De la caillasse plein les poches » va de l'étape 1 à
        // l'étape 6, quand l'ordre alphabétique n'en est pas un.
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
        // Elle vient des prérequis que le site publie : « Les rescapés de
        // Frigost » exige « [FIN] L'essentiel est dans le Lac gelé », donc
        // celle-ci vient avant, ce qu'aucun autre champ du site ne dit.
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
}

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

    [Fact]
    public async Task L_indexation_recopie_le_nom_des_rubriques_dans_les_quetes()
    {
        var (service, _, _) = Build();
        using var _2 = service;

        var catalog = await service.GetAsync(cancellationToken: CancellationToken.None);

        var givre = catalog.Quests.Single(q => q.Title == "Complètement givré");

        Assert.Contains("frigost", givre.SectionKey, StringComparison.Ordinal);

        // Et la recherche s'en sert : c'était tout l'objet de l'opération.
        Assert.Single(service.Search("frigost"));
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
    public async Task Une_quete_nommee_par_deux_pages_va_a_la_plus_precise()
    {
        // Relevé sur le site : dix-sept des dix-huit quêtes des Bulles
        // Temporelles figurent aussi sur la page du Krosmoz, qui les englobe.
        // Prendre la page la plus large laissait la plus précise presque vide.
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
        Assert.Equal(1, krosmoz.Count);
    }
}

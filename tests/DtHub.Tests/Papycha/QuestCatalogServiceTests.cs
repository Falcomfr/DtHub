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
}

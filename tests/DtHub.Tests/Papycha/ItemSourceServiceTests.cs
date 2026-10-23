using System.Text.Json;

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Storage;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Papycha;

public class ItemSourceServiceTests
{
    private readonly FakeDofusTouchClient _client = new();
    private readonly InMemoryDocumentStore<ItemSourcesDocument> _store = new();

    public ItemSourceServiceTests()
    {
        // Every class answers nothing unless a test fills it: the fake would invent rows otherwise.
        foreach (var dataClass in new[] { "Items", "Monsters", "SubAreas", "Recipes", "Skills", "Jobs" })
        {
            _client.Rows[dataClass] = [];
        }
    }

    private ItemSourceService Service() => new(_client, _store);

    private void Item(int id, params int[] droppers) =>
        _client.Rows["Items"][id] = $$"""{"id":{{id}},"nameId":"objet {{id}}","dropMonsterIds":[{{string.Join(',', droppers)}}]}""";

    private void Monster(int id, string name, int[] subAreas, bool archi = false, int miniBoss = 0, params int[] levels) =>
        _client.Rows["Monsters"][id] = $$"""
            {"id":{{id}},"nameId":"{{name}}","isMiniBoss":{{(archi ? "true" : "false")}},"correspondingMiniBossId":{{miniBoss}},
             "subareas":[{{string.Join(',', subAreas)}}],"grades":[{{string.Join(',', levels.Select(l => $$"""{"level":{{l}}}"""))}}]}
            """;

    private void SubArea(int id, string name, params int[] monsters) =>
        _client.Rows["SubAreas"][id] = $$"""{"id":{{id}},"nameId":"{{name}}","monsters":[{{string.Join(',', monsters)}}]}""";

    [Fact]
    public async Task Un_monstre_qui_lache_l_objet_vient_avec_son_niveau_et_ses_zones()
    {
        Item(1, 10);
        Monster(10, "Bouftou", [100, 101], levels: [51, 55, 53]);
        SubArea(100, "Plaine");
        SubArea(101, "Bois");
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        var dropper = Assert.Single(source!.Droppers);
        Assert.Equal(new MonsterSource(10, "Bouftou", 55, false, null, dropper.SubAreas), dropper);
        Assert.Equal(["Plaine", "Bois"], dropper.SubAreas);
        Assert.False(source.NoKnownSource);
    }

    [Fact]
    public async Task Cinq_monstres_au_plus_sont_lus_dans_l_ordre_du_jeu()
    {
        Item(1, 16, 15, 14, 13, 12, 11);
        using var service = Service();

        _ = await service.SourceAsync(1, "fr");

        Assert.Equal([16, 15, 14, 13, 12], _client.Requested["Monsters"].Single());
    }

    [Fact]
    public async Task Un_archimonstre_donne_sa_version_normale_trouvee_dans_sa_zone()
    {
        Item(1, 20);
        Monster(20, "Archi", [100], archi: true, levels: [40]);
        Monster(30, "Normal", [100], miniBoss: 20, levels: [30]);
        Monster(31, "Voisin", [100], levels: [30]);
        SubArea(100, "Village", 20, 30, 31);
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        var archi = Assert.Single(source!.Droppers);
        Assert.True(archi.IsArchimonster);
        Assert.Equal("Normal", archi.NormalName);
        Assert.Equal(["Village"], archi.SubAreas);
        Assert.Equal([30, 31], _client.Requested["Monsters"][1]);
    }

    [Fact]
    public async Task Un_archimonstre_sans_zone_garde_son_nom_sans_liste_de_zones()
    {
        Item(1, 20);
        Monster(20, "Archi", [], archi: true, levels: [40]);
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        var archi = Assert.Single(source!.Droppers);
        Assert.Equal("Archi", archi.Name);
        Assert.Null(archi.NormalName);
        Assert.Empty(archi.SubAreas);
        Assert.False(_client.Requested.ContainsKey("SubAreas"));
    }

    [Fact]
    public async Task Une_ressource_recoltee_donne_le_metier_et_son_niveau()
    {
        Item(1);
        _client.Rows["Skills"][5] = """{"id":5,"parentJobId":2,"gatheredRessourceItem":1,"levelMin":100}""";
        _client.Rows["Skills"][6] = """{"id":6,"parentJobId":43,"gatheredRessourceItem":-1,"levelMin":1}""";
        _client.Rows["Jobs"][2] = """{"id":2,"nameId":"Bûcheron"}""";
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        Assert.Equal([new JobLevel("Bûcheron", 100)], source!.Harvest);
        Assert.Equal([2], _client.Requested["Jobs"].Single());
    }

    [Fact]
    public async Task Un_objet_fabrique_donne_le_metier_de_sa_recette()
    {
        Item(1);
        _client.Rows["Recipes"][1] = """{"resultId":1,"resultLevel":60,"jobId":27,"jobName":"Tailleur"}""";
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        Assert.Equal([new JobLevel("Tailleur", 60)], source!.Craft);
        Assert.False(source.NoKnownSource);
    }

    [Fact]
    public async Task Un_objet_sans_monstre_ni_recolte_ni_recette_n_a_pas_de_source_connue()
    {
        Item(1);
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        Assert.True(source!.NoKnownSource);
        Assert.Empty(source.Droppers);
        Assert.Empty(source.Harvest);
        Assert.Empty(source.Craft);
    }

    [Fact]
    public async Task Une_source_gardee_ne_redemande_rien_et_les_metiers_ne_sont_lus_qu_une_fois()
    {
        Item(1, 10);
        Item(2);
        Monster(10, "Bouftou", [], levels: [5]);
        using var service = Service();
        var first = await service.SourceAsync(1, "fr");
        _client.Requested.Clear();

        Assert.Equal(first, await service.SourceAsync(1, "fr"));
        Assert.Empty(_client.Requested);

        _ = await service.SourceAsync(2, "fr");
        Assert.False(_client.Requested.ContainsKey("Skills"));
    }

    [Fact]
    public async Task Une_source_de_plus_de_trente_jours_est_relue()
    {
        await _store.SaveAsync(new ItemSourcesDocument
        {
            Languages =
            {
                ["fr"] = new ItemSourcesLanguage
                {
                    Items = { [1] = new CachedItemSource(DateTimeOffset.UtcNow.AddDays(-31), new([], [], [], true)) },
                },
            },
        });
        Item(1);
        _client.Rows["Recipes"][1] = """{"resultId":1,"resultLevel":60,"jobName":"Tailleur"}""";
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        Assert.Equal([new JobLevel("Tailleur", 60)], source!.Craft);
    }

    [Fact]
    public async Task Un_echec_rend_null_et_ne_garde_rien()
    {
        Item(1);
        _client.Failure = new HttpRequestException("refus");
        using var service = Service();

        Assert.Null(await service.SourceAsync(1, "fr"));
        Assert.NotNull(service.LastFailure);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task Un_objet_inconnu_du_jeu_rend_null()
    {
        using var service = Service();

        Assert.Null(await service.SourceAsync(1, "fr"));
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public void Le_cache_survit_au_fichier_json()
    {
        var document = new ItemSourcesDocument
        {
            Languages =
            {
                ["fr"] = new ItemSourcesLanguage
                {
                    Harvest = { [1] = [new JobLevel("Bûcheron", 100)] },
                    Items =
                    {
                        [1] = new CachedItemSource(DateTimeOffset.UtcNow, new(
                            [new MonsterSource(10, "Archi", 40, true, "Normal", ["Village"])],
                            [new JobLevel("Bûcheron", 100)],
                            [new JobLevel("Tailleur", 60)],
                            false)),
                    },
                },
            },
        };
        var options = JsonDocumentStore<ItemSourcesDocument>.SerializerOptions;

        var back = JsonSerializer.Deserialize<ItemSourcesDocument>(JsonSerializer.Serialize(document, options), options)!;

        var source = back.Languages["fr"].Items[1].Source;
        Assert.Equal("Normal", source.Droppers.Single().NormalName);
        Assert.Equal(["Village"], source.Droppers.Single().SubAreas);
        Assert.Equal([new JobLevel("Bûcheron", 100)], source.Harvest);
        Assert.Equal([new JobLevel("Tailleur", 60)], source.Craft);
        Assert.Equal([new JobLevel("Bûcheron", 100)], back.Languages["fr"].Harvest[1]);
    }
}

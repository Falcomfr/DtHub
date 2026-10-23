using System.Text.Json;

using DtHub.Core.Papycha;
using DtHub.Core.Storage;
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

    /// <summary>The waits the service asked for between two requests, none of them slept.</summary>
    private readonly List<TimeSpan> _waits = [];

    private Task Wait(TimeSpan wait, CancellationToken ct)
    {
        _waits.Add(wait);

        return Task.CompletedTask;
    }

    private ItemSourceService Service() => new(_client, _store) { Delay = Wait };

    private void Item(int id, params int[] droppers) => Item(id, droppers, []);

    private void Item(int id, int[] droppers, int[] conditional) =>
        _client.Rows["Items"][id] = $$"""
            {"id":{{id}},"nameId":"objet {{id}}","dropMonsterIds":[{{string.Join(',', droppers)}}],
             "conditionnalDropMonsterIds":[{{string.Join(',', conditional)}}]}
            """;

    private void Monster(int id, string name, int[] subAreas, bool archi = false, int miniBoss = 0, params int[] levels) =>
        _client.Rows["Monsters"][id] = $$"""
            {"id":{{id}},"nameId":"{{name}}","isMiniBoss":{{(archi ? "true" : "false")}},"correspondingMiniBossId":{{miniBoss}},
             "subareas":[{{string.Join(',', subAreas)}}],"grades":[{{string.Join(',', levels.Select(l => $$"""{"level":{{l}}}"""))}}]}
            """;

    private void SubArea(int id, string name, params int[] monsters) =>
        _client.Rows["SubAreas"][id] = $$"""{"id":{{id}},"nameId":"{{name}}","monsters":[{{string.Join(',', monsters)}}]}""";

    [Fact]
    public async Task Deux_demandes_au_jeu_sont_espacees_de_deux_secondes_au_moins()
    {
        Item(1, 10);
        Monster(10, "Bouftou", [100], levels: [5]);
        SubArea(100, "Plaine");
        using var service = Service();

        _ = await service.SourceAsync(1, "fr");

        // Skills, Items, Monsters, SubAreas, Recipes: the first goes at once, each next one waits.
        Assert.Equal(_client.Requested.Values.Sum(c => c.Count) - 1, _waits.Count);
        Assert.All(_waits, w => Assert.InRange(w, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Une_lecture_commencee_va_au_bout_et_reste_gardee_quand_le_survol_part()
    {
        Item(1, 10);
        Monster(10, "Bouftou", [], levels: [5]);
        using var hover = new CancellationTokenSource();
        using var service = new ItemSourceService(_client, _store)
        {
            // The hover closes while the read waits before its second request.
            Delay = (_, _) =>
            {
                hover.Cancel();

                return Task.CompletedTask;
            },
        };

        Assert.NotNull(await service.SourceAsync(1, "fr", hover.Token));
        _client.Requested.Clear();

        Assert.NotNull(await service.SourceAsync(1, "fr"));
        Assert.Empty(_client.Requested);
    }

    [Fact]
    public async Task Un_cache_illisible_est_relu_des_qu_il_le_redevient()
    {
        await _store.SaveAsync(new ItemSourcesDocument
        {
            Languages =
            {
                ["fr"] = new ItemSourcesLanguage
                {
                    HarvestReadUtc = DateTimeOffset.UtcNow,
                    Items = { [1] = new CachedItemSource(DateTimeOffset.UtcNow, new([], [], [], true, "gardé")) },
                },
            },
        });
        _store.IsUnread = true;
        using var service = Service();
        _ = await service.SourceAsync(2, "fr");
        _store.IsUnread = false;
        _client.Requested.Clear();

        Assert.Equal("gardé", (await service.SourceAsync(1, "fr"))!.Name);
        Assert.Empty(_client.Requested);
    }

    [Fact]
    public void Une_quete_ne_fait_lire_que_cinq_objets_au_plus()
    {
        TouchObjective[] objectives =
        [
            .. Enumerable.Range(1, 7).Select(i => new TouchObjective(3, [0, i, 1], 0)),
            new TouchObjective(2, [0, 1], 0),
            new TouchObjective(0, [0, 99], 0),
            new TouchObjective(3, [0], 0),
        ];

        Assert.Equal([1, 2, 3, 4, 5], ItemSourceService.ItemsAsked(new TouchQuestFacts([], objectives)));
    }

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
        Assert.Equal("objet 1", source.Name);
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
    public async Task Un_objet_inconnu_du_jeu_rend_null_et_n_est_demande_qu_une_fois()
    {
        using var service = Service();

        Assert.Null(await service.SourceAsync(1, "fr"));
        Assert.Null(await service.SourceAsync(1, "fr"));

        Assert.Single(_client.Requested["Items"]);
        Assert.Empty((await _store.LoadAsync()).Languages["fr"].Items);
    }

    [Fact]
    public async Task Un_echec_apres_la_lecture_des_metiers_les_garde()
    {
        _client.FailingClasses.Add("Items");
        using var service = new ItemSourceService(_client, _store) { FailureCooldown = TimeSpan.Zero, Delay = Wait };
        Assert.Null(await service.SourceAsync(1, "fr"));
        _client.FailingClasses.Clear();
        _client.Requested.Clear();
        Item(1);

        Assert.NotNull(await service.SourceAsync(1, "fr"));
        Assert.False(_client.Requested.ContainsKey("Skills"));
    }

    [Fact]
    public async Task Apres_un_echec_rien_n_est_redemande_pendant_la_pause()
    {
        Item(1);
        _client.Failure = new HttpRequestException("refus");
        using var service = Service();
        _ = await service.SourceAsync(1, "fr");
        _client.Failure = null;
        _client.Requested.Clear();

        Assert.Null(await service.SourceAsync(1, "fr"));
        Assert.Empty(_client.Requested);
    }

    [Fact]
    public async Task Une_lecture_bloquee_abandonne_sans_lever()
    {
        Item(1);
        _client.Pause = new TaskCompletionSource();
        using var service = new ItemSourceService(_client, _store) { ReadTimeout = TimeSpan.FromMilliseconds(50), Delay = Wait };

        Assert.Null(await service.SourceAsync(1, "fr"));
        Assert.NotNull(service.LastFailure);
    }

    [Fact]
    public async Task Les_monstres_conditionnels_suivent_les_autres_et_sont_marques()
    {
        Item(1, [11, 12], [12, 13, 14, 15, 16]);
        Monster(11, "Toujours", [], levels: [5]);
        Monster(12, "Double", [], levels: [5]);
        Monster(13, "Quete", [], levels: [5]);
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        Assert.Equal([11, 12, 13, 14, 15], _client.Requested["Monsters"].Single());
        Assert.Equal([("Toujours", false), ("Double", false), ("Quete", true)], source!.Droppers.Select(d => (d.Name, d.Conditional)));
    }

    [Fact]
    public async Task La_version_normale_qui_lache_aussi_l_objet_est_trouvee_sans_relire()
    {
        Item(1, 20, 30);
        Monster(20, "Archi", [100], archi: true, levels: [40]);
        Monster(30, "Normal", [100], miniBoss: 20, levels: [30]);
        SubArea(100, "Village", 20, 30);
        using var service = Service();

        var source = await service.SourceAsync(1, "fr");

        Assert.Equal("Normal", source!.Droppers[0].NormalName);
        Assert.Single(_client.Requested["Monsters"]);
    }

    [Fact]
    public async Task L_identifiant_du_monstre_vient_de_la_cle()
    {
        Item(1, 10);
        _client.Rows["Monsters"][10] = """{"nameId":"Sans id","grades":[{"level":3}]}""";
        using var service = Service();

        Assert.Equal(10, (await service.SourceAsync(1, "fr"))!.Droppers.Single().Id);
    }

    [Fact]
    public async Task Une_ecriture_ratee_rend_quand_meme_la_source()
    {
        Item(1);
        using var service = new ItemSourceService(_client, new FailingStore()) { Delay = Wait };

        Assert.NotNull(await service.SourceAsync(1, "fr"));
    }

    private sealed class FailingStore : IDocumentStore<ItemSourcesDocument>
    {
        public string FilePath => "(refus)";

        public Task<ItemSourcesDocument> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ItemSourcesDocument());

        public Task SaveAsync(ItemSourcesDocument document, CancellationToken cancellationToken = default) => throw new IOException("disque plein");

        public string Serialize(ItemSourcesDocument document) => "";

        public ItemSourcesDocument? Deserialize(string json) => null;
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

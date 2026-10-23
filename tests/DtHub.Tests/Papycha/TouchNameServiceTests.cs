using DtHub.Core.Papycha;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Papycha;

public class TouchNameServiceTests
{
    private sealed class FakeTable : ITouchIdTableSource
    {
        public TouchIdTable Load() => new()
        {
            Quests = new() { [1] = [10, 11], [2] = [11] },
            Dungeons = new() { [5] = new([50, 51], 7), [6] = new([60]) },
            Series = new() { ["Serie"] = 90 },
            Links = new() { [1] = 91, [2] = 90 },
        };
    }

    private sealed class NoMonsterTable : ITouchIdTableSource
    {
        public TouchIdTable Load() => new()
        {
            Quests = new() { [1] = [10] },
            Dungeons = new() { [6] = new([60]) },
        };
    }

    private readonly FakeDofusTouchClient _client = new();
    private readonly InMemoryDocumentStore<TouchNamesDocument> _store = new();

    private TouchNameService Service() => new(_client, _store, new FakeTable());

    [Fact]
    public async Task Un_premier_rafraichissement_lit_cinq_classes_par_ids_et_les_garde()
    {
        using var service = Service();

        var names = await service.RefreshAsync("en");

        Assert.Equal([10, 11], _client.Requested["Quests"].Single());
        Assert.Equal([50, 51, 60], _client.Requested["Dungeons"].Single());
        Assert.Equal([7], _client.Requested["Monsters"].Single());
        Assert.Equal([1050, 1051, 1060], _client.Requested["Items"].Single());
        Assert.Equal([90, 91], _client.Requested["Achievements"].Single());
        Assert.Equal("nom 10 en", names!.Quests[10].Name);
        Assert.Equal(20, names.Quests[10].LevelMax);
        Assert.Equal([10], names.Quests[10].StepIds);
        Assert.Equal(new TouchDungeon("nom 50 en", 1050), names.Dungeons[50]);
        Assert.Equal("nom 1050 en", names.Items[1050]);

        _client.Requested.Clear();
        var cached = await service.CachedAsync("en");

        Assert.Equal("nom 7 en", cached!.Monsters[7]);
        Assert.Empty(_client.Requested);
    }

    [Fact]
    public async Task Un_cache_de_moins_de_sept_jours_ne_relit_rien()
    {
        using var service = Service();
        _ = await service.RefreshAsync("en");
        _client.Requested.Clear();

        _ = await service.RefreshAsync("en");

        Assert.Empty(_client.Requested);
        Assert.Equal(1, _store.Writes);
    }

    [Fact]
    public async Task Un_cache_de_plus_de_sept_jours_est_relu()
    {
        await _store.SaveAsync(new TouchNamesDocument
        {
            Languages = { ["en"] = new TouchLanguageNames { ReadUtc = DateTimeOffset.UtcNow.AddDays(-8) } },
        });
        using var service = Service();

        var names = await service.RefreshAsync("en");

        Assert.NotEmpty(_client.Requested);
        Assert.Equal("nom 10 en", names!.Quests[10].Name);
    }

    [Fact]
    public async Task Un_refus_garde_le_cache_et_note_l_echec()
    {
        var old = new TouchLanguageNames { ReadUtc = DateTimeOffset.UtcNow.AddDays(-8) };
        await _store.SaveAsync(new TouchNamesDocument { Languages = { ["en"] = old } });
        using var service = Service();
        _client.Failure = new HttpRequestException("refus");

        Assert.Same(old, await service.RefreshAsync("en"));
        Assert.Null(await service.RefreshAsync("fr"));
        Assert.NotNull(service.LastFailure);
    }

    [Fact]
    public async Task Un_tableau_sans_monstre_ne_demande_aucun_monstre()
    {
        using var service = new TouchNameService(_client, _store, new NoMonsterTable());

        _ = await service.RefreshAsync("en");

        Assert.False(_client.Requested.ContainsKey("Monsters"));
    }

    [Fact]
    public async Task Une_lecture_sans_aucun_nom_est_un_echec_et_n_est_pas_gardee()
    {
        using var service = Service();
        _client.Nameless = true;

        Assert.Null(await service.RefreshAsync("en"));
        Assert.NotNull(service.LastFailure);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task Les_noms_sont_gardes_par_langue()
    {
        using var service = Service();

        _ = await service.RefreshAsync("fr");

        Assert.Null(await service.CachedAsync("en"));
        Assert.NotNull(await service.CachedAsync("fr"));
    }

    [Fact]
    public async Task Une_lecture_lente_ne_bloque_pas_le_cache_et_ne_se_double_pas()
    {
        using var service = Service();
        _client.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = service.RefreshAsync("en");
        var second = service.RefreshAsync("en");

        var cached = service.CachedAsync("en");

        Assert.Same(cached, await Task.WhenAny(cached, Task.Delay(5000)));
        Assert.Null(await cached);
        _client.Pause.SetResult();
        Assert.Same(await first, await second);
        Assert.Single(_client.Requested["Quests"]);
    }

    private void GiveSteps()
    {
        _client.Rows["QuestSteps"] = new()
        {
            [10] = """{"id":10,"nameId":"Step ten","descriptionId":"About ten","objectiveIds":[100,101]}""",
            [11] = """{"id":11,"nameId":"Step eleven","descriptionId":"About eleven","objectiveIds":[102]}""",
        };
        _client.Rows["QuestObjectives"] = new()
        {
            [100] = """{"id":100,"stepId":10,"typeId":1,"parameters":[447],"coords":{"_type":"Point","x":-3,"y":-44}}""",
            [101] = """{"id":101,"stepId":10,"typeId":0,"parameters":[662825],"coords":{}}""",
            [102] = """{"id":102,"stepId":11,"typeId":3,"parameters":[447,1746,2],"coords":{}}""",
        };
        _client.Rows["QuestObjectiveTypes"] = new()
        {
            [0] = """{"id":0,"nameId":"#1"}""",
            [1] = """{"id":1,"nameId":"Talk to #1"}""",
            [3] = """{"id":3,"nameId":"Take #3 #2 to #1"}""",
        };
        _client.Rows["Npcs"] = new() { [447] = """{"id":447,"nameId":"Invented NPC"}""" };
        _client.Texts[662825] = "Free text";
    }

    [Fact]
    public async Task Les_etapes_d_une_page_groupee_se_suivent()
    {
        GiveSteps();
        using var service = Service();

        var steps = await service.StepsAsync(1, "en");

        Assert.Equal(["Step ten", "Step eleven"], steps.Select(s => s.Name));
        Assert.Equal("About ten", steps[0].Description);
        Assert.Equal(
            [new TouchObjectiveLine("Talk to Invented NPC", "[-3,-44]"), new TouchObjectiveLine("Free text", "")],
            steps[0].Objectives);
        Assert.Equal("Take 2 nom 1746 en to Invented NPC", steps[1].Objectives.Single().Text);
        Assert.Equal([10, 11], _client.Requested["QuestSteps"].Single());
    }

    [Fact]
    public async Task Les_etapes_lues_ne_se_relisent_pas()
    {
        GiveSteps();
        using var service = Service();
        _ = await service.StepsAsync(1, "en");
        _client.Requested.Clear();

        var again = await service.StepsAsync(1, "en");

        Assert.Equal(2, again.Count);
        Assert.Empty(_client.Requested);
    }

    private const string RewardStep = """{"id":11,"nameId":"e","descriptionId":"d","objectiveIds":[],"optimalLevel":60,"duration":1,"xpRatio":0.8,"kamasRatio":1,"kamasScaleWithPlayerLevel":false,"rewardsIds":[500]}""";

    private void GiveRewardStep(string step)
    {
        GiveSteps();
        _client.Rows["QuestSteps"] = new() { [11] = step };
        _client.Rows["QuestStepRewards"] = new()
        {
            [500] = """{"id":500,"stepId":11,"levelMin":-1,"levelMax":-1,"itemsReward":[[361,2]],"emotesReward":[1],"jobsReward":[],"spellsReward":[],"titlesReward":[],"ornamentsReward":[],"companionsReward":[]}""",
        };
    }

    [Fact]
    public async Task Les_etapes_gardent_leur_recompense_et_le_nom_des_objets()
    {
        GiveRewardStep(RewardStep);
        using var service = Service();
        _ = await service.RefreshAsync("fr");

        var reward = (await service.StepsAsync(2, "fr")).Single().Reward!;

        Assert.Equal((60, 1f, 0.8f, 1f, false), (reward.OptimalLevel, reward.Duration, reward.XpRatio, reward.KamasRatio, reward.KamasScaleWithPlayerLevel));
        var row = reward.Rows.Single();
        Assert.Equal((-1, -1, 1, 0, 0, 0, 0, 0), (row.LevelMin, row.LevelMax, row.Emotes, row.Spells, row.Titles, row.Ornaments, row.Companions, row.Jobs));
        Assert.Equal([new TouchItemReward(361, 2)], row.Items);
        Assert.Equal([500], _client.Requested["QuestStepRewards"].Single());
        Assert.Contains(361, _client.Requested["Items"].Last());
        Assert.Equal("nom 361 fr", (await service.CachedAsync("fr"))!.Items[361]);
    }

    [Fact]
    public async Task Les_noms_d_objets_vont_dans_le_record_vivant_apres_un_rafraichissement_concurrent()
    {
        GiveRewardStep(RewardStep);
        using var service = new TouchNameService(_client, _store, new FakeTable()) { Freshness = TimeSpan.Zero };
        _ = await service.RefreshAsync("fr");
        _client.Pause = new TaskCompletionSource();

        var refresh = service.RefreshAsync("fr");
        var steps = service.StepsAsync(2, "fr");
        await Task.Delay(200);
        _client.Pause.SetResult();
        _ = await refresh;
        _ = await steps;

        Assert.Equal("nom 361 fr", (await service.CachedAsync("fr"))!.Items[361]);
    }

    [Fact]
    public async Task Une_lecture_de_recompenses_en_echec_laisse_les_etapes()
    {
        GiveRewardStep(RewardStep);
        _client.FailingClasses.Add("QuestStepRewards");
        using var service = Service();

        var step = (await service.StepsAsync(2, "fr")).Single();

        Assert.Null(step.Reward);
        Assert.NotNull(service.LastFailure);
    }

    [Fact]
    public async Task Une_etape_sans_champ_de_recompense_reste_sans_recompense()
    {
        GiveRewardStep("""{"id":11,"nameId":"e","descriptionId":"d","objectiveIds":[],"duration":1,"xpRatio":0.8,"kamasRatio":1,"rewardsIds":[500]}""");
        using var service = Service();

        Assert.Null((await service.StepsAsync(2, "fr")).Single().Reward);
        Assert.False(_client.Requested.ContainsKey("QuestStepRewards"));
    }

    [Fact]
    public async Task Un_cache_d_avant_les_recompenses_relit_les_etapes_une_fois()
    {
        GiveRewardStep(RewardStep);
        await _store.SaveAsync(new TouchNamesDocument
        {
            Languages =
            {
                ["fr"] = new TouchLanguageNames
                {
                    ReadUtc = DateTimeOffset.UtcNow,
                    Quests = { [11] = new TouchQuest("q", 1, 2, [11]) },
                    Steps = { [2] = [new TouchStep("old", "", [])] },
                },
            },
        });
        using var service = Service();

        Assert.NotNull((await service.StepsAsync(2, "fr")).Single().Reward);
        _client.Requested.Clear();
        _ = await service.StepsAsync(2, "fr");

        Assert.Empty(_client.Requested);
    }

    [Fact]
    public async Task Une_quete_non_appariee_n_a_pas_d_etapes()
    {
        GiveSteps();
        using var service = Service();

        Assert.Empty(await service.StepsAsync(99, "en"));
        Assert.False(_client.Requested.ContainsKey("QuestSteps"));
    }

    [Fact]
    public async Task Un_refus_du_serveur_donne_une_liste_vide_et_note_l_echec()
    {
        GiveSteps();
        using var service = Service();
        _client.Failure = new HttpRequestException("refus");

        Assert.Empty(await service.StepsAsync(1, "en"));
        Assert.NotNull(service.LastFailure);
    }

    [Fact]
    public async Task Une_lecture_sans_aucune_etape_n_est_pas_gardee()
    {
        GiveSteps();
        _client.Rows["QuestSteps"] = [];
        using var service = Service();

        Assert.Empty(await service.StepsAsync(1, "en"));
        Assert.NotNull(service.LastFailure);
        _client.Requested.Remove("QuestSteps");

        Assert.Empty(await service.StepsAsync(1, "en"));
        Assert.Single(_client.Requested["QuestSteps"]);
    }

    [Fact]
    public async Task Un_rafraichissement_des_noms_efface_les_etapes_gardees()
    {
        GiveSteps();
        using var service = new TouchNameService(_client, _store, new FakeTable()) { Freshness = TimeSpan.Zero };
        _ = await service.StepsAsync(1, "en");
        _ = await service.RefreshAsync("en");
        _client.Requested.Clear();

        var again = await service.StepsAsync(1, "en");

        Assert.Equal(2, again.Count);
        Assert.Single(_client.Requested["QuestSteps"]);
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("es", "es")]
    [InlineData("de", "en")]
    public void La_langue_courante_est_fr_es_ou_en(string spoken, string expected)
    {
        try
        {
            DtHub.Core.Localization.Strings.Speak(System.Globalization.CultureInfo.GetCultureInfo(spoken));

            Assert.Equal(expected, TouchNameService.CurrentLanguage);
        }
        finally
        {
            DtHub.Core.Localization.Strings.Speak(null);
        }
    }

    private const string FactsStepA = """{"id":10,"objectiveIds":[70,71],"optimalLevel":40,"duration":1,"xpRatio":0.5,"kamasRatio":1,"rewardsIds":[500]}""";

    private void SeedFacts()
    {
        _client.Rows["QuestSteps"] = new()
        {
            [10] = FactsStepA,
            [11] = """{"id":11,"objectiveIds":[72],"duration":1}""",
        };
        _client.Rows["QuestObjectives"] = new()
        {
            [70] = """{"typeId":3,"parameters":[5,12]}""",
            [71] = """{"typeId":1,"parameters":[8]}""",
            [72] = """{"typeId":6,"parameters":[9,1]}""",
        };
        _client.Rows["QuestStepRewards"] = new() { [500] = """{"levelMin":1,"levelMax":-1,"itemsReward":[[600,2]]}""" };
    }

    [Fact]
    public async Task Une_lecture_en_masse_demande_les_trois_classes_dans_l_ordre_puis_sert_le_cache()
    {
        SeedFacts();
        using var service = new TouchNameService(_client, _store, new FakeTable()) { FactsPause = TimeSpan.Zero };

        var facts = await service.FactsAsync("en");

        Assert.Equal(["QuestSteps", "QuestObjectives", "QuestStepRewards"], _client.Requested.Keys.Skip(5));
        Assert.Equal([10, 11], _client.Requested["QuestSteps"].Single());
        Assert.Equal([70, 71, 72], _client.Requested["QuestObjectives"].Single());
        Assert.Equal([500], _client.Requested["QuestStepRewards"].Single());
        Assert.Equal(40, facts[1].Steps[0]!.OptimalLevel);
        Assert.Equal(new TouchItemReward(600, 2), facts[1].Steps[0]!.Rows.Single().Items.Single());
        Assert.Equal([3, 1, 6], facts[1].Objectives.Select(o => o.TypeId));
        Assert.Equal([5, 12], facts[1].Objectives[0].Parameters);
        Assert.Equal(40, facts[1].Objectives[0].StepOptimalLevel);

        _client.Requested.Clear();
        _ = await service.FactsAsync("en");

        Assert.Empty(_client.Requested);
    }

    [Fact]
    public async Task Une_etape_sans_niveau_optimal_donne_une_entree_nulle()
    {
        SeedFacts();
        using var service = new TouchNameService(_client, _store, new FakeTable()) { FactsPause = TimeSpan.Zero };

        var facts = await service.FactsAsync("en");

        Assert.Equal(2, facts[1].Steps.Length);
        Assert.NotNull(facts[1].Steps[0]);
        Assert.Null(facts[1].Steps[1]);
    }

    [Fact]
    public async Task Un_refus_des_objectifs_garde_le_cache_precedent()
    {
        SeedFacts();
        var previous = new Dictionary<int, TouchQuestFacts> { [1] = new([], []) };
        await _store.SaveAsync(new TouchNamesDocument
        {
            Languages = { ["en"] = new TouchLanguageNames { ReadUtc = DateTimeOffset.UtcNow, Facts = previous, FactsReadUtc = DateTimeOffset.UtcNow.AddDays(-8) } },
        });
        _client.FailingClasses.Add("QuestObjectives");
        using var service = new TouchNameService(_client, _store, new FakeTable()) { FactsPause = TimeSpan.Zero };

        var facts = await service.FactsAsync("en");

        Assert.Same(previous, facts);
        Assert.NotNull(service.LastFailure);
    }
}

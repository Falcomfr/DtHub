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
}

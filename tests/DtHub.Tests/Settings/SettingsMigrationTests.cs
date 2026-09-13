using DtHub.Core.Settings;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Settings;

/// <summary>
/// Migration of a settings file from one schema version to the next. The raw
/// file is written by hand, like that of a real user.
/// </summary>
public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-migration-" + Guid.NewGuid().ToString("N"));

    private readonly JsonDocumentStore<AppSettingsDocument> _store;
    private readonly SettingsService _service;

    public SettingsMigrationTests()
    {
        _store = new JsonDocumentStore<AppSettingsDocument>(
            Path.Combine(_directory, "settings.json"), NullLogger.Instance);

        _service = new SettingsService(_store);
    }

    public void Dispose()
    {
        _service.Dispose();
        _store.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// The file actually found on the user's machine before the update: two
    /// checked instances, the old sizes, ranks already set.
    /// </summary>
    private const string VersionTrois = """
    {
      "schemaVersion": 3,
      "setupCompleted": true,
      "sizePercentages": [55, 70, 85, 100],
      "sizeIndex": 0,
      "gameAnchor": "MiddleLeft",
      "instances": [
        { "deviceId": "PHONE-A", "userId": 0, "packageName": "com.ankama.dofustouch",
          "userName": "Principal", "isEnabled": true, "order": 0 },
        { "deviceId": "PHONE-A", "userId": 999, "packageName": "com.ankama.dofustouch",
          "userName": "XSpace", "isEnabled": true, "order": 5 }
      ]
    }
    """;

    private async Task WriteAsync(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_store.FilePath, json, CancellationToken.None);
        _service.Invalidate();
    }

    /// <summary>
    /// A file with no version field is read as if it were up to date:
    /// AppSettingsDocument defaults SchemaVersion to the current version, so
    /// no migration step fires.
    ///
    /// This is optimistic, and it is a deliberate choice: the steps only
    /// trigger on specific values, and replaying them on a modern file missing
    /// the field would wrongly change it. This only happens with a
    /// hand-written file. The test exists so that this choice is stated
    /// somewhere rather than being a side effect of a default value.
    /// </summary>
    [Fact]
    public async Task Un_fichier_sans_version_est_tenu_pour_a_jour()
    {
        await WriteAsync("""
        { "sizePercentages": [30, 45, 60, 90], "virtualDisplayWidth": 1080,
          "virtualDisplayHeight": 1920, "virtualDisplayDpi": 320 }
        """);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal([30, 45, 60, 90], settings.SizePercentages);

        // The version 3 migration would have replaced this display.
        Assert.Equal(1080, settings.VirtualDisplayWidth);
    }

    /// <summary>
    /// A file coming from a newer version is brought back down to the current
    /// version. This is the scenario of rolling back after an automatic
    /// update, which the product knows how to do.
    ///
    /// The settings that the next version would have added are lost: they do
    /// not exist in this model, so reading ignores them and the next write
    /// does not restore them. The test pins down this behavior so that a
    /// change would notice it.
    /// </summary>
    [Fact]
    public async Task Un_fichier_venu_d_une_version_plus_recente_est_ramene_a_la_courante()
    {
        await WriteAsync("""
        { "schemaVersion": 99, "sizePercentages": [25, 50, 75, 100],
          "reglageInventeParUneVersionFuture": true }
        """);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal([25, 50, 75, 100], settings.SizePercentages);
    }

    [Fact]
    public async Task Un_fichier_v3_recoit_la_premiere_taille_plus_petite()
    {
        await WriteAsync(VersionTrois);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal([40, 60, 80, 100], settings.SizePercentages);
    }

    [Fact]
    public async Task Des_tailles_choisies_par_l_utilisateur_ne_sont_pas_ecrasees()
    {
        await WriteAsync("""
        { "schemaVersion": 3, "sizePercentages": [30, 45, 60, 90] }
        """);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal([30, 45, 60, 90], settings.SizePercentages);
    }

    [Fact]
    public async Task Les_deux_instances_cochees_le_restent()
    {
        // Without this, the update would lose the automatic launch: the
        // startup set is only rewritten on the first exit through the Quit
        // button.
        await WriteAsync(VersionTrois);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(2, settings.Instances.Count(i => i.IsEnabled));
    }

    [Fact]
    public async Task Les_rangs_creux_sont_resserres()
    {
        await WriteAsync(VersionTrois);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal([0, 1], [.. settings.Instances.Select(i => i.Order).Order()]);
    }

    [Fact]
    public async Task Aucune_geometrie_n_est_inventee_pour_un_fichier_v3()
    {
        // The windows will be placed as before on the first session, then will
        // remember what the user has done with them.
        await WriteAsync(VersionTrois);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.All(settings.Instances, i => Assert.Null(i.Window));
    }

    [Fact]
    public async Task Le_configurateur_est_considere_affiche_pour_un_fichier_v3()
    {
        await WriteAsync(VersionTrois);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.True(settings.ConfiguratorVisible);
    }

    [Fact]
    public async Task Une_ecriture_faite_avant_toute_lecture_migre_quand_meme_le_fichier()
    {
        // The trap: a write that loaded the document without migrating it
        // would stamp it with the current version. The migration was then lost
        // forever, and the ranks stayed sparse.
        await WriteAsync(VersionTrois);

        await _service.UpdateAsync(s => s.SizeIndex = 2, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal([40, 60, 80, 100], settings.SizePercentages);
        Assert.Equal(
            Enumerable.Range(0, settings.Instances.Count),
            settings.Instances.Select(i => i.Order).Order());
    }

    /// <summary>
    /// The real file lost on August 30: schema 8, "Closest" as the zoom, a
    /// tier removed from the code. This is the very file that cost the whole
    /// configuration before the tolerant converter existed.
    /// </summary>
    [Fact]
    public async Task Un_palier_de_zoom_retire_devient_le_palier_voisin()
    {
        await WriteAsync("""
        {
          "schemaVersion": 8,
          "setupCompleted": true,
          "gameZoom": "Closest",
          "quality": "High"
        }
        """);

        var settings = await _service.GetAsync(CancellationToken.None);

        // "Très proche" ("Very close") is merged into "proche" ("close"),
        // which takes over its value.
        Assert.Equal(GameZoom.Close, settings.GameZoom);

        // "Haute" ("High") is merged into the maximum, never downward.
        Assert.Equal(StreamQuality.Maximum, settings.Quality);

        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
    }

    /// <summary>
    /// And the file is not lost either way: the rest is kept.
    /// </summary>
    [Fact]
    public async Task Un_palier_inconnu_ne_fait_pas_perdre_le_fichier()
    {
        await WriteAsync("""
        {
          "schemaVersion": 8,
          "customSizePercent": 43,
          "gameZoom": "PalierQuiNExistePas",
          "instances": [
            { "deviceId": "PHONE-A", "userId": 0, "packageName": "com.ankama.dofustouch",
              "userName": "Principal", "isEnabled": true, "order": 0 }
          ]
        }
        """);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(43, settings.CustomSizePercent);
        Assert.Single(settings.Instances);
        Assert.Equal("Principal", settings.Instances[0].UserName);
    }
}

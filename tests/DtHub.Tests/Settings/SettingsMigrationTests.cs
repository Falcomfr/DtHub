using DtHub.Core.Settings;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Settings;

/// <summary>
/// Passage d'un fichier de réglages d'une version de schéma à la suivante.
/// Le fichier brut est écrit à la main, comme celui d'un utilisateur réel.
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
    /// Le fichier réellement trouvé chez l'utilisateur avant la mise à jour :
    /// deux instances cochées, les anciennes tailles, des rangs déjà posés.
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
        // Sans cela, la mise à jour ferait perdre le lancement automatique :
        // l'ensemble de démarrage n'est réécrit qu'à la première sortie par
        // le bouton Quitter.
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
        // Les fenêtres se placeront comme avant à la première session, puis
        // mémoriseront ce que l'utilisateur en aura fait.
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
        // Le piège : une écriture qui chargeait le document sans le migrer
        // l'estampillait à la version courante. La migration était alors
        // perdue pour toujours, et les rangs restaient creux.
        await WriteAsync(VersionTrois);

        await _service.UpdateAsync(s => s.SizeIndex = 2, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal([40, 60, 80, 100], settings.SizePercentages);
        Assert.Equal(
            Enumerable.Range(0, settings.Instances.Count),
            settings.Instances.Select(i => i.Order).Order());
    }
}

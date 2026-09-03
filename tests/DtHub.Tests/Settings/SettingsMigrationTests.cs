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

    /// <summary>
    /// Un fichier sans champ de version se lit comme s'il était à jour :
    /// AppSettingsDocument donne à SchemaVersion la version courante par
    /// défaut, si bien qu'aucune étape de migration ne tire.
    ///
    /// C'est optimiste et c'est assumé : les étapes ne se déclenchent que sur
    /// des valeurs précises, et les rejouer sur un fichier moderne dont le
    /// champ manque en changerait à tort. Le cas n'arrive que sur un fichier
    /// écrit à la main. L'épreuve existe pour que ce choix soit dit quelque
    /// part plutôt que d'être un effet de bord d'une valeur par défaut.
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

        // La migration de la version 3 aurait remplacé cet afficheur.
        Assert.Equal(1080, settings.VirtualDisplayWidth);
    }

    /// <summary>
    /// Un fichier venu d'une version plus récente est ramené à la version
    /// courante. C'est le scénario du retour en arrière après une mise à jour
    /// automatique, que le produit sait faire.
    ///
    /// Les réglages que la version suivante aurait ajoutés sont perdus : ils
    /// n'existent pas dans ce modèle, donc la lecture les ignore et la
    /// prochaine écriture ne les remet pas. L'épreuve fixe ce comportement pour
    /// qu'un changement s'en aperçoive.
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

    /// <summary>
    /// Le vrai fichier perdu le 30 août : schéma 8, « Closest » comme zoom, un
    /// palier retiré du code. C'est ce fichier-là qui a coûté toute la
    /// configuration avant que le convertisseur tolérant n'existe.
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

        // « Très proche » est fondu dans « proche », qui prend sa valeur.
        Assert.Equal(GameZoom.Close, settings.GameZoom);

        // « Haute » est fondue dans la maximale, jamais vers le dessous.
        Assert.Equal(StreamQuality.Maximum, settings.Quality);

        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
    }

    /// <summary>
    /// Et le fichier ne se perd pas pour autant : le reste est conservé.
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

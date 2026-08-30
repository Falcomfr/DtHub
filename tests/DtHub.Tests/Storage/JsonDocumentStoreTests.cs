using DtHub.Core.Settings;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Storage;

public sealed class JsonDocumentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-store-" + Guid.NewGuid().ToString("N"));

    public sealed record Reglages
    {
        public string Nom { get; set; } = "défaut";
        public int Taille { get; set; } = 80;
        public List<string> Favoris { get; set; } = [];
    }

    private string Path0 => Path.Combine(_directory, "reglages.json");

    private JsonDocumentStore<Reglages> Store() =>
        new(Path0, NullLogger.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Un_fichier_absent_donne_un_document_par_defaut()
    {
        using var store = Store();

        var document = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("défaut", document.Nom);
        Assert.Equal(80, document.Taille);
    }

    [Fact]
    public async Task Ce_qui_est_ecrit_est_relu_a_l_identique()
    {
        using var store = Store();
        var written = new Reglages { Nom = "Jeux", Taille = 90, Favoris = ["a", "b"] };

        await store.SaveAsync(written, CancellationToken.None);
        var read = await store.LoadAsync(CancellationToken.None);

        // L'égalité de record compare la liste par référence : on vérifie donc
        // champ par champ, ce qui teste réellement l'aller-retour JSON.
        Assert.Equal(written.Nom, read.Nom);
        Assert.Equal(written.Taille, read.Taille);
        Assert.Equal(written.Favoris, read.Favoris);
    }

    [Fact]
    public async Task Les_dossiers_manquants_sont_crees_a_l_ecriture()
    {
        var nested = Path.Combine(_directory, "a", "b", "reglages.json");
        using var store = new JsonDocumentStore<Reglages>(nested, NullLogger.Instance);

        await store.SaveAsync(new Reglages(), CancellationToken.None);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task Un_json_corrompu_est_archive_et_une_configuration_valide_est_recreee()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path0, "{ ceci n'est pas du json", CancellationToken.None);

        using var store = Store();
        var document = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("défaut", document.Nom);

        var archived = Directory.GetFiles(_directory, "reglages.json.corrompu-*");
        Assert.Single(archived);
        Assert.Contains("ceci n'est pas du json", await File.ReadAllTextAsync(archived[0], CancellationToken.None), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_fichier_vide_donne_un_document_par_defaut_sans_archivage()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path0, "   \n", CancellationToken.None);

        using var store = Store();
        var document = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("défaut", document.Nom);
        Assert.Empty(Directory.GetFiles(_directory, "*.corrompu-*"));
    }

    [Fact]
    public async Task Apres_archivage_une_ecriture_repart_sur_un_fichier_sain()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path0, "{{{", CancellationToken.None);

        using var store = Store();
        await store.LoadAsync(CancellationToken.None);
        await store.SaveAsync(new Reglages { Nom = "Travail" }, CancellationToken.None);

        Assert.Equal("Travail", (await store.LoadAsync(CancellationToken.None)).Nom);
    }

    [Fact]
    public async Task Le_fichier_ecrit_reste_lisible_par_un_humain()
    {
        using var store = Store();
        await store.SaveAsync(new Reglages { Nom = "Jeux" }, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path0, CancellationToken.None);

        Assert.Contains("\n", json, StringComparison.Ordinal);
        Assert.Contains("\"nom\": \"Jeux\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aucun_fichier_temporaire_ne_subsiste_apres_ecriture()
    {
        using var store = Store();

        await store.SaveAsync(new Reglages(), CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_directory, "*.nouveau"));
    }

    [Fact]
    public async Task Des_ecritures_concurrentes_laissent_un_fichier_valide()
    {
        using var store = Store();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            store.SaveAsync(new Reglages { Nom = $"n{i}", Taille = i }, CancellationToken.None)));

        var document = await store.LoadAsync(CancellationToken.None);

        Assert.StartsWith("n", document.Nom, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_champ_inconnu_dans_le_fichier_ne_fait_pas_echouer_la_lecture()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path0,
            """{ "nom": "Jeux", "reglageDUneVersionFuture": 42 }""",
            CancellationToken.None);

        using var store = Store();

        Assert.Equal("Jeux", (await store.LoadAsync(CancellationToken.None)).Nom);
    }

    [Fact]
    public async Task Un_palier_retire_du_code_ne_fait_pas_perdre_le_reste_du_fichier()
    {
        // Le convertisseur standard refusait le fichier entier sur ce seul
        // mot : retirer un palier de qualité effaçait les instances, les
        // raccourcis et la géométrie des fenêtres de tous ceux qui l'avaient
        // choisi. C'est arrivé, et le fichier ne doit plus jamais partir pour
        // si peu.
        Directory.CreateDirectory(_directory);

        var path = System.IO.Path.Combine(_directory, "vieux.json");

        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 9,
              "setupCompleted": true,
              "quality": "Haute",
              "gameZoom": "TresProche",
              "customSizePercent": 62
            }
            """);

        var store = new JsonDocumentStore<AppSettingsDocument>(path, NullLogger.Instance);

        var document = await store.LoadAsync(CancellationToken.None);

        // Le reste du fichier est intact...
        Assert.True(document.SetupCompleted);
        Assert.Equal(62, document.CustomSizePercent);

        // ... et les deux valeurs inconnues retombent sur leur repli déclaré.
        Assert.Equal(StreamQuality.Maximum, document.Quality);
        Assert.Equal(GameZoom.Normal, document.GameZoom);

        // Le fichier n'a pas été mis en quarantaine : il n'était pas corrompu.
        Assert.Empty(Directory.GetFiles(_directory, "*.corrompu-*"));
    }
}

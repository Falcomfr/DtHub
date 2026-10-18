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

        // Record equality compares the list by reference: we
        // therefore check field by field, which actually tests the
        // JSON round trip.
        Assert.Equal(written.Nom, read.Nom);
        Assert.Equal(written.Taille, read.Taille);
        Assert.Equal(written.Favoris, read.Favoris);
    }

    [Fact]
    public async Task Un_document_inchange_n_est_pas_reecrit()
    {
        // Seen on 2026-10-17: devices.json rewritten every four to six
        // seconds and settings.json every thirty while the panel showed,
        // most of the time with nothing changed.
        using var store = Store();
        var document = new Reglages { Nom = "Jeux" };

        await store.SaveAsync(document, CancellationToken.None);
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path0, old);

        await store.SaveAsync(document with { }, CancellationToken.None);

        Assert.Equal(old, File.GetLastWriteTimeUtc(Path0));
    }

    [Fact]
    public async Task Un_fichier_change_par_ailleurs_est_bien_remplace()
    {
        using var store = Store();
        var document = new Reglages { Nom = "Jeux" };

        await store.SaveAsync(document, CancellationToken.None);
        await File.WriteAllTextAsync(Path0, """{ "nom": "modifié à la main" }""");

        await store.SaveAsync(document, CancellationToken.None);

        Assert.Equal("Jeux", (await store.LoadAsync(CancellationToken.None)).Nom);
    }

    [Fact]
    public async Task Un_document_change_est_reecrit()
    {
        using var store = Store();

        await store.SaveAsync(new Reglages { Nom = "Jeux" }, CancellationToken.None);
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path0, old);

        await store.SaveAsync(new Reglages { Nom = "Autre" }, CancellationToken.None);

        Assert.NotEqual(old, File.GetLastWriteTimeUtc(Path0));
        Assert.Equal("Autre", (await store.LoadAsync(CancellationToken.None)).Nom);
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
        // The standard converter used to reject the entire file over
        // this single word: removing a quality tier erased the
        // instances, the shortcuts, and the window geometry of
        // everyone who had chosen it. This happened, and the file
        // must never again be thrown away for something this small.
        Directory.CreateDirectory(_directory);

        var path = System.IO.Path.Combine(_directory, "vieux.json");

        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 9,
              "quality": "Haute",
              "gameZoom": "TresProche",
              "customSizePercent": 62
            }
            """);

        var store = new JsonDocumentStore<AppSettingsDocument>(path, NullLogger.Instance);

        var document = await store.LoadAsync(CancellationToken.None);

        // The rest of the file is intact...
        Assert.Equal(62, document.CustomSizePercent);

        // ... and the two unknown values fall back to their declared
        // default, which carries the decision made when the tier
        // was removed: "Haute" ("High") merges into the maximum,
        // "très proche" ("very close") into "proche" ("close"). Two
        // migrations claimed to handle this; they never fired, since
        // this converter had already settled it by the time they
        // ran, and one of the two targeted "proche" while the
        // fallback said "normal".
        Assert.Equal(StreamQuality.Maximum, document.Quality);
        Assert.Equal(GameZoom.Close, document.GameZoom);

        // The file was not quarantined: it was not corrupted.
        Assert.Empty(Directory.GetFiles(_directory, "*.corrompu-*"));
    }

    [Fact]
    public async Task Un_fichier_illisible_n_est_jamais_ecrase_par_les_valeurs_par_defaut()
    {
        // Held by an antivirus or by the other copy of DT Hub at startup:
        // the defaults it yields must not replace the real file on the
        // first save.
        using (var writer = Store())
        {
            await writer.SaveAsync(new Reglages { Nom = "Jeux" }, CancellationToken.None);
        }

        using var store = Store();
        Reglages loaded;

        using (new FileStream(Path0, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            loaded = await store.LoadAsync(CancellationToken.None);
        }

        Assert.Equal("défaut", loaded.Nom);

        await store.SaveAsync(loaded, CancellationToken.None);

        Assert.Contains("Jeux", await File.ReadAllTextAsync(Path0), StringComparison.Ordinal);
    }

    public sealed record AvecModificateurs
    {
        public DtHub.Core.Hotkeys.HotkeyModifiers Modifiers { get; set; }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(64)]
    public void Un_modificateur_hors_des_valeurs_connues_retombe_sans_faire_tomber_la_lecture(int value)
    {
        // A hand-edited file: -1 used to throw an overflow, which no reader
        // catches, and the settings did not load at all.
        var read = System.Text.Json.JsonSerializer.Deserialize<AvecModificateurs>(
            $"{{\"modifiers\": {value}}}", JsonDocumentStore<AvecModificateurs>.SerializerOptions);

        Assert.Equal(DtHub.Core.Hotkeys.HotkeyModifiers.None, read!.Modifiers);
    }
}

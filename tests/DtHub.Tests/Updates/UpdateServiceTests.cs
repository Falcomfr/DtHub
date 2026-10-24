using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DtHub.Core.Localization;
using DtHub.Core.Updates;
using DtHub.Infrastructure.Updates;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Updates;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dthub-maj-" + Guid.NewGuid().ToString("N"));

    private readonly FakeAppPaths _paths;
    private readonly string _executable;

    public UpdateServiceTests()
    {
        _paths = new FakeAppPaths(_root);
        _paths.EnsureCreated();

        var program = Path.Combine(_root, "programme");
        _ = Directory.CreateDirectory(program);
        _executable = Path.Combine(program, "DtHub.exe");
        File.WriteAllText(_executable, "ancienne version");
    }

    public void Dispose()
    {
        Strings.Speak(null);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder that resists deletion should not fail
            // a test.
        }
    }

    private static byte[] Neuf => Encoding.UTF8.GetBytes("nouvelle version");

    private static string Empreinte(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private (UpdateService Service, FakeReleaseSource Source) Monter(
        string digest,
        Version version,
        IReadOnlyDictionary<string, string>? noteUrls = null,
        TimeSpan? noteWait = null)
    {
        var source = new FakeReleaseSource
        {
            Latest = new AppRelease(
                version, "### Ajouté\n- Une chose.", "https://exemple/exe", 16, "https://exemple/sha")
            {
                NoteUrls = noteUrls ?? new Dictionary<string, string>(),
            },
        };

        _ = source.WithFile("https://exemple/exe", Neuf);
        _ = source.WithText("https://exemple/sha", digest + "  DtHub.exe\n");

        var service = new UpdateService(
            source,
            _paths,
            new UpdateTarget(new Version(0, 1, 0), _executable),
            NullLogger<UpdateService>.Instance,
            noteWait);

        return (service, source);
    }

    [Fact]
    public async Task Prepare_et_pose_une_livraison_plus_recente()
    {
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        Assert.True(service.Ready);
        Assert.True(service.Apply(automatic: true));

        Assert.Equal("nouvelle version", File.ReadAllText(_executable));
        Assert.Equal(
            "ancienne version",
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(_executable)!, UpdatePaths.Retired)));
    }

    [Fact]
    public async Task Prepare_la_copie_a_cote_de_l_executable_pour_que_la_pose_ne_fasse_que_renommer()
    {
        // The download lives in the user's folder, the executable wherever
        // the player put it (D10): a move across disks is a copy that a
        // shutdown can cut short, leaving a truncated DtHub.exe.
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        var incoming = Path.Combine(Path.GetDirectoryName(_executable)!, UpdatePaths.Incoming);
        Assert.Equal("nouvelle version", File.ReadAllText(incoming));

        // Apply no longer reads the download, only the copy beside the executable.
        File.Delete(UpdatePaths.Staged(_paths.UpdatesDirectory, new Version(0, 2, 0)));

        Assert.True(service.Apply(automatic: true));
        Assert.Equal("nouvelle version", File.ReadAllText(_executable));
        Assert.False(File.Exists(incoming));
    }

    [Fact]
    public async Task Garde_le_telechargement_quand_la_copie_a_cote_de_l_executable_echoue()
    {
        // A folder DT Hub cannot write to: a directory in the copy's place
        // refuses it the same way. Losing the download would cost sixty
        // megabytes at every startup, and never become ready.
        var incoming = Path.Combine(Path.GetDirectoryName(_executable)!, UpdatePaths.Incoming);
        _ = Directory.CreateDirectory(incoming);
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        Assert.False(service.Ready);
        Assert.True(File.Exists(UpdatePaths.Staged(_paths.UpdatesDirectory, new Version(0, 2, 0))));

        Directory.Delete(incoming);
        var (suivant, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await suivant.CheckAsync(automatic: true);

        Assert.True(suivant.Ready);
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task Un_telechargement_coupe_est_refait_au_demarrage_suivant()
    {
        // Quitting mid-download left a truncated DtHub-x.y.z.exe, taken as
        // downloaded by the next startup, refused for its digest and deleted:
        // the update waited for a third startup. The exception stands for
        // the process ending, after which nothing runs.
        var (coupe, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0));
        _ = source.WithFailure("https://exemple/exe", new InvalidOperationException("Arrêté."));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => coupe.CheckAsync(automatic: true));

        var (suivant, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await suivant.CheckAsync(automatic: true);

        Assert.True(suivant.Ready);
    }

    [Fact]
    public async Task Ne_pose_pas_une_livraison_prete_quand_la_case_a_ete_decochee()
    {
        // Prepared at startup, the box unchecked before quitting: the player
        // wants to keep the version he has.
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        Assert.True(service.Ready);
        Assert.False(service.Apply(automatic: false));
        Assert.Equal("ancienne version", File.ReadAllText(_executable));
    }

    [Fact]
    public async Task Refuse_une_livraison_dont_l_empreinte_ne_correspond_pas()
    {
        // The downloaded file does not stay on disk: an executable
        // that does not match what the feed announces has no business
        // being there.
        var (service, _) = Monter(new string('a', 64), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        Assert.False(service.Ready);
        Assert.False(service.Apply(automatic: true));
        Assert.Equal("ancienne version", File.ReadAllText(_executable));
        Assert.Empty(Directory.GetFiles(_paths.UpdatesDirectory, "*.exe"));
    }

    [Fact]
    public async Task Ignore_une_livraison_qui_n_est_pas_plus_recente()
    {
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 1, 0));

        await service.CheckAsync(automatic: true);

        Assert.Null(service.Available);
        Assert.False(service.Ready);
    }

    [Fact]
    public async Task Ne_prepare_rien_quand_la_mise_a_jour_n_est_pas_automatique()
    {
        var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: false);

        Assert.NotNull(service.Available);
        Assert.False(service.Ready);
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task Refuse_de_poser_dans_un_arbre_de_sources()
    {
        // The development launcher republishes on every startup: an
        // update placed there would be overwritten within the second.
        File.WriteAllText(Path.Combine(_root, "DtHub.slnx"), string.Empty);

        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        Assert.Null(service.Available);
        Assert.False(service.Apply(automatic: true));
        Assert.Equal("ancienne version", File.ReadAllText(_executable));
    }

    [Fact]
    public async Task Ecrit_la_note_de_version_et_ne_la_rend_qu_une_fois()
    {
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        // The note waits for the startup that will run the new version.
        var posee = new UpdateService(
            new FakeReleaseSource(),
            _paths,
            new UpdateTarget(new Version(0, 2, 0), _executable),
            NullLogger<UpdateService>.Instance);

        var notes = posee.TakeNotes();

        Assert.Contains("Une chose.", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("###", notes, StringComparison.Ordinal);
        Assert.Empty(posee.TakeNotes());
    }

    private static readonly Dictionary<string, string> Traduites = new()
    {
        ["fr"] = "https://exemple/notes.fr.md",
        ["es"] = "https://exemple/notes.es.md",
    };

    [Theory]
    [InlineData("fr", "Une chose, en français.")]
    [InlineData("fr-FR", "Une chose, en français.")]
    [InlineData("es-ES", "Una cosa, en español.")]
    public async Task Annonce_la_note_dans_la_langue_parlee(string langue, string attendu)
    {
        Strings.Speak(CultureInfo.GetCultureInfo(langue));
        var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
        _ = source.WithText("https://exemple/notes.fr.md", "### Ajouté\n- Une chose, en français.");
        _ = source.WithText("https://exemple/notes.es.md", "### Añadido\n- Una cosa, en español.");

        // Manual check: the "update available" window reads Available too.
        await service.CheckAsync(automatic: false);

        Assert.Contains(attendu, service.Available!.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Garde_l_anglais_quand_la_note_traduite_ne_repond_pas()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("fr"));
        var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
        _ = source.WithFailure("https://exemple/notes.fr.md");

        await service.CheckAsync(automatic: false);

        Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
    }

    [Fact]
    public async Task Garde_l_anglais_quand_la_note_traduite_leve_une_autre_erreur()
    {
        // A response whose charset .NET does not know: ReadAsStringAsync
        // throws InvalidOperationException, not HttpRequestException.
        Strings.Speak(CultureInfo.GetCultureInfo("fr"));
        var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
        _ = source.WithFailure("https://exemple/notes.fr.md", new InvalidOperationException("Jeu de caractères inconnu."));

        await service.CheckAsync(automatic: false);

        Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
    }

    [Fact]
    public async Task Ne_retient_pas_la_livraison_pour_une_note_qui_tarde()
    {
        // The updates client waits ten minutes: a note that never answers
        // must not hold the update back that long.
        Strings.Speak(CultureInfo.GetCultureInfo("fr"));
        var (service, source) = Monter(
            Empreinte(Neuf), new Version(0, 2, 0), Traduites, TimeSpan.FromMilliseconds(50));
        _ = source.WithHang("https://exemple/notes.fr.md");

        var check = service.CheckAsync(automatic: false);

        Assert.Same(check, await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
    }

    [Fact]
    public async Task Rend_l_annulation_demandee_pendant_la_lecture_de_la_note()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("fr"));
        var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
        _ = source.WithHang("https://exemple/notes.fr.md");
        using var annulation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAsync(automatic: true, annulation.Token));

        Assert.Null(service.Available);
    }

    [Fact]
    public async Task Garde_l_anglais_quand_la_note_traduite_est_vide()
    {
        // FakeReleaseSource answers an empty string for an unknown URL.
        Strings.Speak(CultureInfo.GetCultureInfo("fr"));
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);

        await service.CheckAsync(automatic: false);

        Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
    }

    [Fact]
    public async Task En_anglais_garde_le_corps_de_la_livraison()
    {
        // Both translations are served: English must still not pick one.
        Strings.Speak(CultureInfo.GetCultureInfo("en"));
        var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
        _ = source.WithText("https://exemple/notes.fr.md", "- Une chose, en français.");
        _ = source.WithText("https://exemple/notes.es.md", "- Una cosa, en español.");

        await service.CheckAsync(automatic: false);

        Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
    }

    [Fact]
    public async Task Ecrit_la_note_traduite_pour_le_demarrage_suivant()
    {
        Strings.Speak(CultureInfo.GetCultureInfo("fr"));
        var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
        _ = source.WithText("https://exemple/notes.fr.md", "### Ajouté\n- Une chose, en français.");

        await service.CheckAsync(automatic: true);

        var posee = new UpdateService(
            new FakeReleaseSource(),
            _paths,
            new UpdateTarget(new Version(0, 2, 0), _executable),
            NullLogger<UpdateService>.Instance);

        Assert.Contains("Une chose, en français.", posee.TakeNotes(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Balaie_l_ancien_executable_et_les_livraisons_depassees()
    {
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);
        _ = service.Apply(automatic: true);

        // A copy prepared and never placed, the box unchecked meanwhile.
        var incoming = Path.Combine(Path.GetDirectoryName(_executable)!, UpdatePaths.Incoming);
        File.WriteAllText(incoming, "nouvelle version");

        var suivante = new UpdateService(
            new FakeReleaseSource(),
            _paths,
            new UpdateTarget(new Version(0, 2, 0), _executable),
            NullLogger<UpdateService>.Instance);

        suivante.Sweep();

        Assert.False(File.Exists(
            Path.Combine(Path.GetDirectoryName(_executable)!, UpdatePaths.Retired)));
        Assert.False(File.Exists(incoming));
        Assert.Empty(Directory.GetFiles(_paths.UpdatesDirectory, "DtHub-*.exe"));
    }

    [Fact]
    public void Balaie_un_telechargement_abandonne()
    {
        // Quit mid-download, then a newer release came out: that part was
        // never overwritten, and sixty megabytes stayed for good. Sweep runs
        // at startup, before any download.
        var part = UpdatePaths.Staged(_paths.UpdatesDirectory, new Version(0, 3, 0)) + ".part";
        File.WriteAllText(part, "moitié");

        new UpdateService(
            new FakeReleaseSource(),
            _paths,
            new UpdateTarget(new Version(0, 1, 0), _executable),
            NullLogger<UpdateService>.Instance).Sweep();

        Assert.False(File.Exists(part));
    }
}

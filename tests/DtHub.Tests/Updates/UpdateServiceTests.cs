using System.Security.Cryptography;
using System.Text;

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
        Version version)
    {
        var source = new FakeReleaseSource
        {
            Latest = new AppRelease(
                version, "### Ajouté\n- Une chose.", "https://exemple/exe", 16, "https://exemple/sha"),
        };

        _ = source.WithFile("https://exemple/exe", Neuf);
        _ = source.WithText("https://exemple/sha", digest + "  DtHub.exe\n");

        var service = new UpdateService(
            source,
            _paths,
            new UpdateTarget(new Version(0, 1, 0), _executable),
            NullLogger<UpdateService>.Instance);

        return (service, source);
    }

    [Fact]
    public async Task Prepare_et_pose_une_livraison_plus_recente()
    {
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);

        Assert.True(service.Ready);
        Assert.True(service.Apply());

        Assert.Equal("nouvelle version", File.ReadAllText(_executable));
        Assert.Equal(
            "ancienne version",
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(_executable)!, UpdatePaths.Retired)));
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
        Assert.False(service.Apply());
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
        Assert.False(service.Apply());
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

    [Fact]
    public async Task Balaie_l_ancien_executable_et_les_livraisons_depassees()
    {
        var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0));

        await service.CheckAsync(automatic: true);
        _ = service.Apply();

        var suivante = new UpdateService(
            new FakeReleaseSource(),
            _paths,
            new UpdateTarget(new Version(0, 2, 0), _executable),
            NullLogger<UpdateService>.Instance);

        suivante.Sweep();

        Assert.False(File.Exists(
            Path.Combine(Path.GetDirectoryName(_executable)!, UpdatePaths.Retired)));
        Assert.Empty(Directory.GetFiles(_paths.UpdatesDirectory, "DtHub-*.exe"));
    }
}

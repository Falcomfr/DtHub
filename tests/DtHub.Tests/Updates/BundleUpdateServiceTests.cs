using System.Security.Cryptography;

using DtHub.Core.Updates;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Updates;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Updates;

/// <summary>
/// The Mac's update, on real bundles: unpacked by ditto, checked by
/// codesign, moved as the shutdown moves them.
/// </summary>
public sealed class BundleUpdateServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dthub-maj-mac-").FullName;
    private readonly FakeAppPaths _paths;
    private readonly string _bundle;

    public BundleUpdateServiceTests()
    {
        _paths = new FakeAppPaths(_root);
        _paths.EnsureCreated();
        _bundle = Path.Combine(_root, "Applications", "DT Hub.app");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder that resists deletion should not fail a test.
        }
    }

    /// <summary>A signed bundle whose executable is a real Mach-O, marked with a version file.</summary>
    private static void MakeBundle(string bundle, string mark)
    {
        var macOs = Path.Combine(bundle, "Contents", "MacOS");
        _ = Directory.CreateDirectory(macOs);
        File.Copy("/usr/bin/true", Path.Combine(macOs, BundleUpdateService.Executable), overwrite: true);
        File.WriteAllText(
            Path.Combine(bundle, "Contents", "Info.plist"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict>"
            + "<key>CFBundleIdentifier</key><string>io.github.falcomfr.dthub.tests</string>"
            + $"<key>CFBundleExecutable</key><string>{BundleUpdateService.Executable}</string>"
            + "<key>CFBundlePackageType</key><string>APPL</string></dict></plist>");
        _ = Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
        File.WriteAllText(Path.Combine(bundle, "Contents", "Resources", "version.txt"), mark);
        Run("/usr/bin/codesign", "--force", "--deep", "--sign", "-", bundle);
    }

    private static void Run(string file, params string[] arguments)
    {
        using var process = System.Diagnostics.Process.Start(file, arguments);
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>The release archive, built as the pipeline builds it.</summary>
    private byte[] Archive(string mark, Action<string>? spoil = null)
    {
        var staging = Path.Combine(_root, "fabrique-" + Guid.NewGuid().ToString("N"), "DT Hub.app");
        MakeBundle(staging, mark);
        spoil?.Invoke(staging);
        var zip = staging + ".zip";
        Run("/usr/bin/ditto", "-c", "-k", "--keepParent", staging, zip);

        return File.ReadAllBytes(zip);
    }

    private BundleUpdateService Service(byte[] archive, string? digest = null, string? bundle = null)
    {
        var source = new FakeReleaseSource
        {
            Latest = new AppRelease(new Version(0, 13, 0), "- Une chose.", "https://exemple/zip", 0, "https://exemple/sha"),
        };

        _ = source.WithFile("https://exemple/zip", archive);
        _ = source.WithText(
            "https://exemple/sha",
            (digest ?? Convert.ToHexStringLower(SHA256.HashData(archive))) + "  DtHub-mac.zip\n");

        return new BundleUpdateService(
            source, _paths, new ProcessRunner(), new Version(0, 12, 2), bundle ?? _bundle,
            NullLogger<BundleUpdateService>.Instance);
    }

    private string Mark() => File.ReadAllText(Path.Combine(_bundle, "Contents", "Resources", "version.txt"));

    [Fact]
    public void Reconnait_l_application_d_apres_son_executable()
    {
        Assert.Equal(
            "/Applications/DT Hub.app",
            BundleUpdateService.BundleOf("/Applications/DT Hub.app/Contents/MacOS/DtHub.Mac", _ => false));
        Assert.Equal(string.Empty, BundleUpdateService.BundleOf("/opt/dthub/DtHub.Mac", _ => false));
        Assert.Equal(string.Empty, BundleUpdateService.BundleOf(null, _ => false));
    }

    [Fact]
    public void N_offre_rien_a_une_application_sortie_d_un_arbre_de_sources()
    {
        // The build would overwrite the update within the minute.
        Assert.Equal(
            string.Empty,
            BundleUpdateService.BundleOf(
                "/Dev/DtHub/src/DtHub.Mac/bin/DT Hub.app/Contents/MacOS/DtHub.Mac",
                path => path == "/Dev/DtHub/DtHub.slnx"));
    }

    [MacFact]
    public async Task Prepare_puis_pose_la_nouvelle_application_a_la_fermeture()
    {
        MakeBundle(_bundle, "ancienne");
        var service = Service(Archive("nouvelle"));

        await service.CheckAsync(automatic: true);

        Assert.True(service.Ready);
        Assert.Equal("ancienne", Mark());

        Assert.True(service.Apply(automatic: true));
        Assert.Equal("nouvelle", Mark());
        Run("/usr/bin/codesign", "--verify", "--deep", "--strict", _bundle);

        // The notes wait for the next startup, the old bundle for the sweep.
        Assert.True(File.Exists(UpdatePaths.Notes(_paths.UpdatesDirectory, new Version(0, 13, 0))));
        Assert.True(Directory.Exists(Path.Combine(_paths.UpdatesDirectory, "DT Hub.app.ancien")));

        service.Sweep();

        Assert.False(Directory.Exists(Path.Combine(_paths.UpdatesDirectory, "DT Hub.app.ancien")));
    }

    [MacFact]
    public async Task Ne_pose_rien_quand_la_case_a_ete_decochee_entre_temps()
    {
        MakeBundle(_bundle, "ancienne");
        var service = Service(Archive("nouvelle"));

        await service.CheckAsync(automatic: true);

        Assert.False(service.Apply(automatic: false));
        Assert.Equal("ancienne", Mark());
    }

    [MacFact]
    public async Task Annonce_sans_telecharger_quand_la_case_est_decochee()
    {
        MakeBundle(_bundle, "ancienne");
        var service = Service(Archive("nouvelle"));

        await service.CheckAsync(automatic: false);

        Assert.NotNull(service.Available);
        Assert.False(service.Ready);
        Assert.False(service.Apply(automatic: true));
    }

    [MacFact]
    public async Task Refuse_une_archive_dont_l_empreinte_ne_correspond_pas()
    {
        MakeBundle(_bundle, "ancienne");
        var service = Service(Archive("nouvelle"), digest: new string('0', 64));

        await service.CheckAsync(automatic: true);

        Assert.False(service.Ready);
        Assert.False(service.Apply(automatic: true));
        Assert.Equal("ancienne", Mark());
        Assert.Empty(Directory.EnumerateFiles(_paths.UpdatesDirectory, "DtHub-mac-*"));
    }

    [MacFact]
    public async Task Refuse_une_application_dont_la_signature_ne_tient_plus()
    {
        // Touched after signing: a bundle macOS would refuse to start must
        // not replace one that starts.
        MakeBundle(_bundle, "ancienne");
        var service = Service(Archive(
            "nouvelle",
            staging => File.WriteAllText(Path.Combine(staging, "Contents", "Resources", "version.txt"), "abîmée")));

        await service.CheckAsync(automatic: true);

        Assert.False(service.Ready);
        Assert.Equal("ancienne", Mark());
    }

    [MacFact]
    public async Task Refuse_une_archive_qui_ne_contient_pas_l_application()
    {
        MakeBundle(_bundle, "ancienne");
        var other = Path.Combine(_root, "autre", "Autre.app");
        MakeBundle(other, "nouvelle");
        var zip = other + ".zip";
        Run("/usr/bin/ditto", "-c", "-k", "--keepParent", other, zip);
        var service = Service(File.ReadAllBytes(zip));

        await service.CheckAsync(automatic: true);

        Assert.False(service.Ready);
        Assert.Equal("ancienne", Mark());
    }

    [MacFact]
    public async Task Remet_l_ancienne_application_quand_la_nouvelle_ne_peut_prendre_sa_place()
    {
        MakeBundle(_bundle, "ancienne");
        var service = Service(Archive("nouvelle"));
        await service.CheckAsync(automatic: true);

        // The unpacked bundle gone between preparation and shutdown.
        Directory.Delete(Path.Combine(_paths.UpdatesDirectory, "nouvelle", "DT Hub.app"), recursive: true);

        Assert.False(service.Apply(automatic: true));
        Assert.Equal("ancienne", Mark());
    }

    [Fact]
    public async Task N_offre_rien_hors_d_une_application()
    {
        var service = Service([1, 2, 3], bundle: string.Empty);

        await service.CheckAsync(automatic: true);

        Assert.Null(service.Available);
    }

    [MacFact]
    public async Task Le_menage_garde_une_archive_plus_recente_et_jette_les_autres()
    {
        MakeBundle(_bundle, "ancienne");
        var folder = _paths.UpdatesDirectory;
        File.WriteAllText(Path.Combine(folder, "DtHub-mac-0.12.1.zip"), "vieille");
        File.WriteAllText(Path.Combine(folder, "DtHub-mac-0.13.0.zip"), "prochaine");
        File.WriteAllText(Path.Combine(folder, "DtHub-mac-0.13.0.zip.part"), "coupée");

        Service([]).Sweep();

        Assert.False(File.Exists(Path.Combine(folder, "DtHub-mac-0.12.1.zip")));
        Assert.True(File.Exists(Path.Combine(folder, "DtHub-mac-0.13.0.zip")));
        Assert.False(File.Exists(Path.Combine(folder, "DtHub-mac-0.13.0.zip.part")));
    }
}

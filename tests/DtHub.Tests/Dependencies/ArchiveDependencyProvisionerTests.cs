using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;
using DtHub.Infrastructure.Storage;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Dependencies;

public sealed class ArchiveDependencyProvisionerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dthub-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held by the antivirus must not make a
            // test that succeeded blush. UpdateServiceTests has
            // the same guard.
        }
    }

    /// <summary>
    /// Builds an archive containing <c>outil/outil.exe</c>.
    /// </summary>
    private static byte[] BuildArchive(string content = "faux exécutable")
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("outil/outil.exe");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return buffer.ToArray();
    }

    private const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode Plain = UnixFileMode.UserRead | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode AnyExecute =
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>
    /// Builds a .tar.gz as scrcpy's macOS release is: <c>outil/outil</c>
    /// with the given mode, and <c>outil/outil-server</c>, a plain file
    /// that must stay plain.
    /// </summary>
    private static byte[] BuildTarGz(UnixFileMode executableMode)
    {
        using var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true))
        {
            tar.WriteEntry(new UstarTarEntry(TarEntryType.Directory, "outil/") { Mode = Executable });
            tar.WriteEntry(Entry("outil/outil", "faux exécutable", executableMode));
            tar.WriteEntry(Entry("outil/outil-server", "fausse donnée", Plain));
        }

        return buffer.ToArray();

        static UstarTarEntry Entry(string name, string content, UnixFileMode mode) => new(TarEntryType.RegularFile, name)
        {
            Mode = mode,
            DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
        };
    }

    /// <summary>
    /// A zip as Google's macOS platform-tools is, or as a Windows tool
    /// makes one: with the Unix modes recorded, or with none.
    /// </summary>
    private static byte[] BuildUnixZip(UnixFileMode? executableMode)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("outil/outil");
            if (executableMode is { } mode)
            {
                // The high sixteen bits hold the mode, as Info-ZIP writes it.
                entry.ExternalAttributes = (int)mode << 16;
            }

            using var writer = new StreamWriter(entry.Open());
            writer.Write("faux exécutable");
        }

        return buffer.ToArray();
    }

    private static ExternalDependency UnixDependency(byte[] archive, string extension) =>
        Dependency(archive) with
        {
            Url = new Uri($"https://exemple.invalid/outil-1.2.3{extension}"),
            Executable = "outil",
        };

    private static ExternalDependency Dependency(byte[] archive, string? sha256 = null, long? size = null) => new()
    {
        Key = "outil",
        DisplayName = "Outil de test",
        Version = "1.2.3",
        Url = new Uri("https://exemple.invalid/outil-1.2.3.zip"),
        SizeBytes = size ?? archive.Length,
        Sha256 = sha256 ?? Convert.ToHexStringLower(SHA256.HashData(archive)),
        ArchiveRootDirectory = "outil",
        Executable = "outil.exe",
        License = "Licence de test",
        Redistributable = true,
    };

    private (ArchiveDependencyProvisioner Provisioner, FakeHttpMessageHandler Handler) Build(
        byte[] archive,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(archive, statusCode);
        var provisioner = new ArchiveDependencyProvisioner(
            new HttpClient(handler),
            new AppPaths(_root),
            NullLogger<ArchiveDependencyProvisioner>.Instance);

        return (provisioner, handler);
    }

    [Fact]
    public async Task Une_archive_conforme_est_extraite_et_l_executable_est_rendu()
    {
        var archive = BuildArchive();
        var (provisioner, handler) = Build(archive);

        var path = await provisioner.EnsureAvailableAsync(Dependency(archive), null, CancellationToken.None);

        Assert.True(File.Exists(path), $"Attendu présent : {path}");
        Assert.EndsWith(Path.Combine("outil-1.2.3", "outil", "outil.exe"), path, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Une_empreinte_non_conforme_interrompt_l_installation()
    {
        var archive = BuildArchive();
        var (provisioner, _) = Build(archive);
        var dependency = Dependency(archive, sha256: new string('0', 64));

        var exception = await Assert.ThrowsAsync<DependencyProvisioningException>(
            () => provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None));

        Assert.Contains("empreinte", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(provisioner.TryGetExistingPath(dependency));
    }

    [Fact]
    public async Task Une_taille_inattendue_interrompt_l_installation_avant_le_calcul_d_empreinte()
    {
        var archive = BuildArchive();
        var (provisioner, _) = Build(archive);
        var dependency = Dependency(archive, size: archive.Length + 512);

        var exception = await Assert.ThrowsAsync<DependencyProvisioningException>(
            () => provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None));

        Assert.Contains("taille", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Une_url_en_clair_est_refusee_sans_emettre_de_requete()
    {
        var archive = BuildArchive();
        var (provisioner, handler) = Build(archive);
        var dependency = Dependency(archive) with { Url = new Uri("http://exemple.invalid/outil.zip") };

        await Assert.ThrowsAsync<DependencyProvisioningException>(
            () => provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Un_echec_http_est_signale_proprement()
    {
        var (provisioner, _) = Build([], HttpStatusCode.NotFound);
        var dependency = Dependency(BuildArchive());

        var exception = await Assert.ThrowsAsync<DependencyProvisioningException>(
            () => provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None));

        Assert.Contains("404", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_archive_sans_l_executable_attendu_est_rejetee()
    {
        var archive = BuildArchive();
        var (provisioner, _) = Build(archive);
        var dependency = Dependency(archive) with { Executable = "absent.exe" };

        var exception = await Assert.ThrowsAsync<DependencyProvisioningException>(
            () => provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None));

        Assert.Contains("absent.exe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_installation_deja_presente_ne_declenche_aucun_telechargement()
    {
        var archive = BuildArchive();
        var (provisioner, handler) = Build(archive);
        var dependency = Dependency(archive);

        var first = await provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None);
        var second = await provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task L_avancement_traverse_les_etapes_jusqu_a_la_fin()
    {
        var archive = BuildArchive();
        var (provisioner, _) = Build(archive);
        var stages = new List<ProvisioningStage>();

        // A synchronous reporter, not Progress<T>: this one posts
        // to the synchronization context, which used to force
        // waiting two hundred milliseconds before concluding. This
        // was the only clock-based wait in the repository, and the
        // prime candidate for an intermittent red build on a loaded
        // runner.
        var progress = new SyncProgress<ProvisioningProgress>(p => stages.Add(p.Stage));

        await provisioner.EnsureAvailableAsync(Dependency(archive), progress, CancellationToken.None);

        Assert.Contains(ProvisioningStage.Downloading, stages);
        Assert.Contains(ProvisioningStage.Verifying, stages);
        Assert.Contains(ProvisioningStage.Extracting, stages);
        Assert.Contains(ProvisioningStage.Done, stages);
    }

    [Fact]
    public void Aucun_chemin_n_est_rendu_tant_que_rien_n_est_installe()
    {
        var archive = BuildArchive();
        var (provisioner, _) = Build(archive);

        Assert.Null(provisioner.TryGetExistingPath(Dependency(archive)));
    }

    [Fact]
    public async Task Une_archive_tar_gz_est_extraite_avec_son_dossier_racine()
    {
        var archive = BuildTarGz(Executable);
        var (provisioner, handler) = Build(archive);
        var dependency = UnixDependency(archive, ".tar.gz");

        var path = await provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "tools", "outil-1.2.3", "outil", "outil"), path);
        Assert.Equal("faux exécutable", await File.ReadAllTextAsync(path));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "outil-server")));
        Assert.Equal(1, handler.RequestCount);

        // Found in place on the next launch, without a request.
        Assert.Equal(path, provisioner.TryGetExistingPath(dependency));
    }

    [Fact]
    public async Task Une_archive_tar_gz_alteree_est_refusee_comme_un_zip()
    {
        var archive = BuildTarGz(Executable);
        var (provisioner, _) = Build(archive);
        var dependency = UnixDependency(archive, ".tar.gz") with { Sha256 = new string('0', 64) };

        await Assert.ThrowsAsync<DependencyProvisioningException>(
            () => provisioner.EnsureAvailableAsync(dependency, null, CancellationToken.None));

        Assert.Null(provisioner.TryGetExistingPath(dependency));
    }

    [UnixFact]
    public async Task Les_modes_d_une_archive_tar_gz_sont_conserves()
    {
        var archive = BuildTarGz(Executable);
        var (provisioner, _) = Build(archive);

        var path = await provisioner.EnsureAvailableAsync(
            UnixDependency(archive, ".tar.gz"), null, CancellationToken.None);

        Assert.Equal(AnyExecute, File.GetUnixFileMode(path) & AnyExecute);

        // The server is pushed to the phone, never run here: it stays plain.
        var server = Path.Combine(Path.GetDirectoryName(path)!, "outil-server");
        Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(server) & AnyExecute);
    }

    [UnixFact]
    public async Task L_executable_d_une_archive_tar_gz_sans_droit_d_execution_est_rendu_executable()
    {
        var archive = BuildTarGz(Plain);
        var (provisioner, _) = Build(archive);

        var path = await provisioner.EnsureAvailableAsync(
            UnixDependency(archive, ".tar.gz"), null, CancellationToken.None);

        Assert.Equal(AnyExecute, File.GetUnixFileMode(path) & AnyExecute);
    }

    [UnixFact]
    public async Task Les_modes_d_un_zip_unix_sont_conserves()
    {
        var archive = BuildUnixZip(Executable);
        var (provisioner, _) = Build(archive);

        var path = await provisioner.EnsureAvailableAsync(
            UnixDependency(archive, ".zip"), null, CancellationToken.None);

        Assert.Equal(Executable, File.GetUnixFileMode(path) & Executable);
    }

    [UnixFact]
    public async Task L_executable_d_un_zip_sans_modes_est_rendu_executable()
    {
        var archive = BuildUnixZip(executableMode: null);
        var (provisioner, _) = Build(archive);

        var path = await provisioner.EnsureAvailableAsync(
            UnixDependency(archive, ".zip"), null, CancellationToken.None);

        Assert.Equal(AnyExecute, File.GetUnixFileMode(path) & AnyExecute);
    }
}

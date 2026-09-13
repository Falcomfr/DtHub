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
            // Un fichier encore tenu par l'antivirus ne doit pas faire rougir
            // une épreuve qui a réussi. UpdateServiceTests a la même garde.
        }
    }

    /// <summary>Construit une archive contenant <c>outil/outil.exe</c>.</summary>
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

        // Un rapporteur synchrone, et non Progress<T> : celui-ci poste sur le
        // contexte de synchronisation, ce qui obligeait à attendre deux cents
        // millisecondes avant de conclure. C'était la seule attente d'horloge
        // du dépôt, et le premier candidat au rouge intermittent sur un
        // coureur chargé.
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
}

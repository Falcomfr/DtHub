using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;
using DtHub.Infrastructure.Storage;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Dependencies;

/// <summary>
/// The Mac's locator: DT Hub's own copy first, downloaded on first need,
/// the player's installed copy only when that download fails.
/// </summary>
public sealed class ProvisionedToolTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dthub-provisionne-" + Guid.NewGuid().ToString("N"));

    private readonly string _installed;

    public ProvisionedToolTests()
    {
        _installed = Path.Combine(_root, "brew");
        Directory.CreateDirectory(_installed);
    }

    private readonly List<ProvisionedTool> _tools = [];

    public void Dispose()
    {
        foreach (var tool in _tools)
        {
            tool.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Same guard as ArchiveDependencyProvisionerTests.
        }
    }

    private static byte[] BuildArchive()
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("outil/adb");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("faux adb");
        }

        return buffer.ToArray();
    }

    private static ExternalDependency Dependency(byte[] archive) => new()
    {
        Key = "outil-macos",
        DisplayName = "Outil de test",
        Version = "1.2.3",
        Url = new Uri("https://exemple.invalid/outil-1.2.3-darwin.zip"),
        SizeBytes = archive.Length,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(archive)),
        ArchiveRootDirectory = "outil",
        Executable = "adb",
        License = "Licence de test",
        Redistributable = false,
    };

    private (ProvisionedTool Tool, FakeHttpMessageHandler Handler) Build(
        HttpStatusCode statusCode,
        bool installed)
    {
        var archive = BuildArchive();
        var handler = new FakeHttpMessageHandler(statusCode == HttpStatusCode.OK ? archive : [], statusCode);
        var provisioner = new ArchiveDependencyProvisioner(
            new HttpClient(handler),
            new AppPaths(Path.Combine(_root, "data")),
            NullLogger<ArchiveDependencyProvisioner>.Instance);

        if (installed)
        {
            File.WriteAllText(Path.Combine(_installed, "adb"), string.Empty);
        }

        var tool = new ProvisionedTool(
            provisioner,
            Dependency(archive),
            new InstalledTool("adb", "android-platform-tools", [_installed]),
            NullLogger<ProvisionedTool>.Instance);
        _tools.Add(tool);

        return (tool, handler);
    }

    [Fact]
    public async Task La_copie_de_dt_hub_passe_avant_celle_installee()
    {
        var (tool, handler) = Build(HttpStatusCode.OK, installed: true);

        var path = await tool.GetAdbPathAsync();

        Assert.Equal(Path.Combine(_root, "data", "tools", "outil-macos-1.2.3", "outil", "adb"), path);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("1.2.3", tool.Version);
    }

    [Fact]
    public async Task Un_telechargement_impossible_se_replie_sur_la_copie_installee()
    {
        var (tool, _) = Build(HttpStatusCode.ServiceUnavailable, installed: true);

        var path = await tool.GetAdbPathAsync();

        Assert.Equal(Path.Combine(_installed, "adb"), path);
        Assert.Equal("installed", tool.Version);
    }

    [Fact]
    public async Task Le_repli_tient_pour_toute_la_session()
    {
        // Two adb in one session would fight over the ADB server.
        var (tool, handler) = Build(HttpStatusCode.ServiceUnavailable, installed: true);

        var first = await tool.GetScrcpyPathAsync();
        var second = await tool.GetScrcpyPathAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Sans_telechargement_ni_copie_installee_adb_est_indisponible()
    {
        var (tool, _) = Build(HttpStatusCode.ServiceUnavailable, installed: false);

        var error = await Assert.ThrowsAsync<AdbException>(() => tool.GetAdbPathAsync());

        Assert.Equal(AdbErrorKind.AdbUnavailable, error.Kind);
        Assert.Contains("503", error.UserMessage, StringComparison.Ordinal);
        Assert.Null(tool.TryGetInstalledPath());
    }

    [Fact]
    public async Task Sans_telechargement_ni_copie_installee_scrcpy_dit_pourquoi()
    {
        var (tool, _) = Build(HttpStatusCode.ServiceUnavailable, installed: false);

        var error = await Assert.ThrowsAsync<DependencyProvisioningException>(() => tool.GetScrcpyPathAsync());

        Assert.Contains("503", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rien_n_est_telecharge_pour_savoir_ce_qui_est_en_place()
    {
        var (tool, handler) = Build(HttpStatusCode.OK, installed: true);

        Assert.Equal(Path.Combine(_installed, "adb"), tool.TryGetInstalledPath());
        Assert.Equal(0, handler.RequestCount);
    }
}

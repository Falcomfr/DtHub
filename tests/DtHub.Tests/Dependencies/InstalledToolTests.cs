using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Tests.Dependencies;

/// <summary>
/// The tools installed by the player, on macOS through Homebrew and on
/// Linux from scrcpy's official archive, in place of the downloaded Windows
/// archives.
/// </summary>
public sealed class InstalledToolTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dthub-outils-" + Guid.NewGuid().ToString("N"));

    public InstalledToolTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task L_outil_est_trouve_dans_le_premier_dossier_qui_le_contient()
    {
        var path = Path.Combine(_root, "scrcpy");
        await File.WriteAllTextAsync(path, string.Empty);

        var tool = new InstalledTool("scrcpy", "brew install scrcpy", [Path.Combine(_root, "absent"), _root]);

        Assert.Equal(path, await tool.GetScrcpyPathAsync());
        Assert.Equal(path, tool.TryGetInstalledPath());
    }

    [Fact]
    public async Task Un_scrcpy_absent_dit_comment_l_installer()
    {
        var tool = new InstalledTool("scrcpy", "brew install scrcpy", [_root]);

        var error = await Assert.ThrowsAsync<DependencyProvisioningException>(() => tool.GetScrcpyPathAsync());

        Assert.Contains("brew install scrcpy", error.Message, StringComparison.Ordinal);
        Assert.Null(tool.TryGetInstalledPath());
    }

    [Fact]
    public async Task Un_adb_absent_est_une_erreur_adb_qui_dit_comment_l_installer()
    {
        var tool = new InstalledTool("adb", "brew install android-platform-tools", [_root]);

        var error = await Assert.ThrowsAsync<AdbException>(() => tool.GetAdbPathAsync());

        Assert.Equal(AdbErrorKind.AdbUnavailable, error.Kind);
        Assert.Contains("brew install android-platform-tools", error.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_dossier_local_de_l_utilisateur_passe_avant_le_reste()
    {
        // Where the Linux hint says to put scrcpy's archive, and where the
        // Mac session linked its tools: a Finder or desktop launch does not
        // always have it in PATH.
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

        Assert.Equal(local, InstalledTool.DefaultDirectories()[0]);
    }
}

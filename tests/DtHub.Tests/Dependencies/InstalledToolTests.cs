using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Tests.Dependencies;

/// <summary>
/// The tools installed by the player, as on macOS through Homebrew, in
/// place of the downloaded Windows archives.
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

        var tool = new InstalledTool("scrcpy", "scrcpy", [Path.Combine(_root, "absent"), _root]);

        Assert.Equal(path, await tool.GetScrcpyPathAsync());
        Assert.Equal(path, tool.TryGetInstalledPath());
    }

    [Fact]
    public async Task Un_scrcpy_absent_dit_comment_l_installer()
    {
        var tool = new InstalledTool("scrcpy", "scrcpy", [_root]);

        var error = await Assert.ThrowsAsync<DependencyProvisioningException>(() => tool.GetScrcpyPathAsync());

        Assert.Contains("brew install scrcpy", error.Message, StringComparison.Ordinal);
        Assert.Null(tool.TryGetInstalledPath());
    }

    [Fact]
    public async Task Un_adb_absent_est_une_erreur_adb_qui_dit_comment_l_installer()
    {
        var tool = new InstalledTool("adb", "android-platform-tools", [_root]);

        var error = await Assert.ThrowsAsync<AdbException>(() => tool.GetAdbPathAsync());

        Assert.Equal(AdbErrorKind.AdbUnavailable, error.Kind);
        Assert.Contains("brew install android-platform-tools", error.UserMessage, StringComparison.Ordinal);
    }
}

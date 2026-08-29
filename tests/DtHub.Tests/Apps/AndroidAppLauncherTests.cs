using DtHub.Core.Apps;
using DtHub.Core.Users;
using DtHub.Infrastructure.Storage;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Apps;

public sealed class AndroidAppLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-launch-" + Guid.NewGuid().ToString("N"));

    private readonly JsonDocumentStore<AppCatalogDocument> _store;

    public AndroidAppLauncherTests()
    {
        _store = new JsonDocumentStore<AppCatalogDocument>(
            Path.Combine(_directory, "apps.json"), NullLogger.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private (AndroidAppLauncher Launcher, AppDiscoveryService Apps) Build(FakeAdbClient adb)
    {
        var apps = new AppDiscoveryService(adb, NullAppLabelProvider.Instance, _store);
        return (new AndroidAppLauncher(adb, new AndroidUserService(adb), apps), apps);
    }

    [Fact]
    public async Task L_application_est_lancee_sur_le_profil_et_l_afficheur_demandes()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", "Users:\n\tUserInfo{0:P:c13} running\n\tUserInfo{999:Clone:1030} running\n")
            .WithShell("am start", "Starting: Intent { cmp=com.exemple.app/.Main }");

        var (launcher, _) = Build(adb);

        var result = await launcher.LaunchAsync(
            "USB0001", 999, "com.exemple.app", "com.exemple.app/.Main", 7, CancellationToken.None);

        Assert.True(result.Succeeded);

        var command = adb.ShellCalls.Last(c => c.StartsWith("am start", StringComparison.Ordinal));
        Assert.Contains("--user 999", command, StringComparison.Ordinal);
        Assert.Contains("--display 7", command, StringComparison.Ordinal);
        Assert.Contains("-n com.exemple.app/.Main", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sans_afficheur_l_option_correspondante_est_omise()
    {
        var adb = new FakeAdbClient().WithShell("am start", "Starting: Intent { }");
        var (launcher, _) = Build(adb);

        await launcher.LaunchAsync("USB0001", 0, "com.exemple.app", "com.exemple.app/.Main", null, CancellationToken.None);

        Assert.DoesNotContain("--display", adb.ShellCalls[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_profil_arrete_est_demarre_avant_le_lancement()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", "Users:\n\tUserInfo{0:P:c13} running\n\tUserInfo{999:Clone:1030}\n")
            .WithShell("am start-user", "Success: user started")
            .WithShell("am start ", "Starting: Intent { }");

        var (launcher, _) = Build(adb);

        await launcher.LaunchAsync("USB0001", 999, "com.exemple.app", "com.exemple.app/.Main", 7, CancellationToken.None);

        Assert.Contains(adb.ShellCalls, c => c.StartsWith("am start-user 999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Le_profil_principal_n_est_jamais_demarre_explicitement()
    {
        var adb = new FakeAdbClient().WithShell("am start", "Starting: Intent { }");
        var (launcher, _) = Build(adb);

        await launcher.LaunchAsync("USB0001", 0, "com.exemple.app", "com.exemple.app/.Main", null, CancellationToken.None);

        Assert.DoesNotContain(adb.ShellCalls, c => c.StartsWith("am start-user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_composant_perime_est_resolu_a_nouveau_puis_relance()
    {
        // Cas réel : l'application a été mise à jour et son activité principale
        // a changé de nom.
        var adb = new StatefulFakeAdb();
        var apps = new AppDiscoveryService(adb, NullAppLabelProvider.Instance, _store);
        var launcher = new AndroidAppLauncher(adb, new AndroidUserService(adb), apps);

        var result = await launcher.LaunchAsync(
            "USB0001", 0, "com.exemple.app", "com.exemple.app/.AncienneActivite", 7, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(
            adb.Calls,
            c => c.Contains("-n com.exemple.app/com.exemple.app.NouvelleActivite", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_paquet_disparu_donne_un_message_comprehensible()
    {
        var adb = new FakeAdbClient()
            .WithShell("am start", "Error: Activity class {com.exemple.app/.Main} does not exist.")
            .WithShell("resolve-activity", "No activity found");

        var (launcher, _) = Build(adb);

        var result = await launcher.LaunchAsync(
            "USB0001", 0, "com.exemple.app", "com.exemple.app/.Main", 7, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("plus installée", result.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sans_composant_connu_la_resolution_est_tentee()
    {
        var adb = new FakeAdbClient()
            .WithShell("resolve-activity", "com.exemple.app/.Main")
            .WithShell("am start ", "Starting: Intent { }");

        var (launcher, _) = Build(adb);

        var result = await launcher.LaunchAsync(
            "USB0001", 0, "com.exemple.app", null, 7, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// Faux ADB dont la résolution rend une activité différente de celle
    /// mémorisée, et qui n'accepte que la nouvelle.
    /// </summary>
    private sealed class StatefulFakeAdb : FakeAdbClientBase
    {
        public override string Shell(string joined) => joined switch
        {
            var c when c.Contains("resolve-activity", StringComparison.Ordinal) =>
                "com.exemple.app/.NouvelleActivite",
            var c when c.Contains("NouvelleActivite", StringComparison.Ordinal) =>
                "Starting: Intent { }",
            var c when c.StartsWith("am start", StringComparison.Ordinal) =>
                "Error: Activity class {com.exemple.app/.AncienneActivite} does not exist.",
            _ => string.Empty,
        };
    }
}

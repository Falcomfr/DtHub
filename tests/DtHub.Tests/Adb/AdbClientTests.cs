using DtHub.Core.Adb;
using DtHub.Infrastructure.Adb;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Adb;

public class AdbClientTests
{
    private static (AdbClient Client, FakeProcessRunner Runner) Build(FakeProcessRunner runner) =>
        (new AdbClient(runner, new FakeAdbLocator(), NullLogger<AdbClient>.Instance), runner);

    [Fact]
    public async Task La_liste_detaillee_demande_bien_devices_moins_l()
    {
        var (client, runner) = Build(new FakeProcessRunner()
            .Respond("devices -l", "List of devices attached\n0123 device model:Pixel_9\n"));

        var devices = await client.ListDevicesAsync(detailed: true, CancellationToken.None);

        Assert.Equal("devices -l", runner.LastArguments);
        Assert.Equal("Pixel_9", Assert.Single(devices).Model);
    }

    [Fact]
    public async Task La_liste_courte_n_ajoute_pas_l_option_detaillee()
    {
        var (client, runner) = Build(new FakeProcessRunner()
            .Respond("devices", "List of devices attached\n0123\tdevice\n"));

        await client.ListDevicesAsync(detailed: false, CancellationToken.None);

        Assert.Equal("devices", runner.LastArguments);
    }

    [Fact]
    public async Task Une_commande_ciblee_est_prefixee_par_le_numero_de_serie()
    {
        var (client, runner) = Build(new FakeProcessRunner().Respond("reconnect", "done\n"));

        await client.ExecuteAsync("192.168.1.25:5555", ["reconnect"], null, null, CancellationToken.None);

        Assert.Equal("-s 192.168.1.25:5555 reconnect", runner.LastArguments);
        Assert.Equal(@"C:\Dev\DTHub\adb\adb.exe", runner.Calls[^1].FileName);
    }

    [Fact]
    public async Task Le_shell_transmet_chaque_argument_separement()
    {
        var (client, runner) = Build(new FakeProcessRunner().Respond("pm list users", "Users:\n"));

        await client.ShellAsync("0123", ["pm", "list", "users"], null, CancellationToken.None);

        Assert.Equal(["-s", "0123", "shell", "pm", "list", "users"], runner.Calls[^1].Arguments);
    }

    [Fact]
    public async Task Un_appareil_non_autorise_donne_un_message_actionnable()
    {
        var (client, _) = Build(new FakeProcessRunner()
            .Respond("shell", standardError: "adb: device unauthorized.\nThis adb server's $ADB_VENDOR_KEYS is not set", exitCode: 1));

        var exception = await Assert.ThrowsAsync<AdbException>(
            () => client.ShellAsync("0123", ["getprop"], null, CancellationToken.None));

        Assert.Equal(AdbErrorKind.DeviceUnauthorized, exception.Kind);
        Assert.Contains("acceptez la demande de débogage", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_appareil_absent_est_distingue_d_un_appareil_hors_ligne()
    {
        var (notFound, _) = Build(new FakeProcessRunner()
            .Respond("shell", standardError: "adb: device '0123' not found", exitCode: 1));
        var (offline, _) = Build(new FakeProcessRunner()
            .Respond("shell", standardError: "error: device offline", exitCode: 1));

        var first = await Assert.ThrowsAsync<AdbException>(
            () => notFound.ShellAsync("0123", ["getprop"], null, CancellationToken.None));
        var second = await Assert.ThrowsAsync<AdbException>(
            () => offline.ShellAsync("0123", ["getprop"], null, CancellationToken.None));

        Assert.Equal(AdbErrorKind.DeviceNotFound, first.Kind);
        Assert.Equal(AdbErrorKind.DeviceOffline, second.Kind);
    }

    [Fact]
    public async Task Un_depassement_de_delai_est_signale_comme_tel()
    {
        var (client, _) = Build(new FakeProcessRunner()
            .Respond("shell", exitCode: -1, timedOut: true));

        var exception = await Assert.ThrowsAsync<AdbException>(
            () => client.ShellAsync("0123", ["getprop"], null, CancellationToken.None));

        Assert.Equal(AdbErrorKind.Timeout, exception.Kind);
    }

    [Fact]
    public async Task Un_shell_qui_rend_zero_mais_signale_une_erreur_est_traite_comme_un_echec()
    {
        // Le shell Android rend souvent 0 même quand la commande a échoué.
        var (client, _) = Build(new FakeProcessRunner()
            .Respond("shell", standardOutput: "Error: Unknown package: com.exemple.app\n"));

        var exception = await Assert.ThrowsAsync<AdbException>(
            () => client.ShellAsync("0123", ["pm", "path", "com.exemple.app"], null, CancellationToken.None));

        Assert.Equal(AdbErrorKind.PackageNotFound, exception.Kind);
    }

    [Fact]
    public async Task Un_adb_introuvable_donne_une_erreur_comprehensible()
    {
        var (client, _) = Build(new FakeProcessRunner().FailToLaunch());

        var exception = await Assert.ThrowsAsync<AdbException>(
            () => client.ListDevicesAsync(true, CancellationToken.None));

        Assert.Equal(AdbErrorKind.AdbUnavailable, exception.Kind);

        // Le message disait « consultez le diagnostic dans les paramètres »,
        // page qui n'a jamais existé. Il renvoie maintenant au signalement,
        // qui existe et qui porte le refus technique.
        Assert.Contains("Signalez", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Les_proprietes_systeme_sont_lues_et_analysees()
    {
        var (client, _) = Build(new FakeProcessRunner().Respond("getprop", """
            [ro.product.manufacturer]: [Xiaomi]
            [ro.build.version.sdk]: [34]
            """));

        var properties = await client.GetPropertiesAsync("0123", CancellationToken.None);

        Assert.Equal("Xiaomi", properties["ro.product.manufacturer"]);
        Assert.Equal("34", properties["ro.build.version.sdk"]);
    }

    [Fact]
    public async Task La_version_d_adb_rend_la_premiere_ligne()
    {
        var (client, _) = Build(new FakeProcessRunner().Respond("version", """
            Android Debug Bridge version 1.0.41
            Version 35.0.2-12147458
            """));

        Assert.Equal("Android Debug Bridge version 1.0.41", await client.GetVersionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Demarrer_et_arreter_le_serveur_envoient_les_bonnes_commandes()
    {
        var (client, runner) = Build(new FakeProcessRunner()
            .Respond("start-server").Respond("kill-server"));

        await client.StartServerAsync(CancellationToken.None);
        Assert.Equal("start-server", runner.LastArguments);

        await client.StopServerAsync(CancellationToken.None);
        Assert.Equal("kill-server", runner.LastArguments);
    }

    [Fact]
    public async Task L_attente_d_un_appareil_rend_faux_si_elle_expire()
    {
        var (client, _) = Build(new FakeProcessRunner()
            .Respond("wait-for-device", exitCode: -1, timedOut: true));

        Assert.False(await client.WaitForDeviceAsync("0123", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task Le_code_d_appairage_n_apparait_jamais_dans_la_ligne_de_commande_journalisee()
    {
        var (client, runner) = Build(new FakeProcessRunner()
            .Respond("pair", "Successfully paired to 192.168.1.25:37123 [guid=adb-MATERIEL123-nJyLWZ]"));

        await client.PairAsync("192.168.1.25", 37123, "654321", CancellationToken.None);

        var request = runner.Calls[^1];

        // L'argument est bien transmis au processus...
        Assert.Contains("654321", request.Arguments);

        // ...mais la ligne destinée aux journaux le masque.
        Assert.DoesNotContain("654321", request.ToDisplayString(), StringComparison.Ordinal);
        Assert.Contains("***", request.ToDisplayString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_appairage_reussi_rend_l_identifiant_renvoye_par_adb()
    {
        var (client, _) = Build(new FakeProcessRunner()
            .Respond("pair", "Successfully paired to 192.168.1.25:37123 [guid=adb-MATERIEL123-nJyLWZ]"));

        var result = await client.PairAsync("192.168.1.25", 37123, "654321", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("adb-MATERIEL123-nJyLWZ", result.DeviceGuid);
    }

    [Fact]
    public async Task Un_appairage_expire_est_traite_comme_un_echec_et_non_comme_un_succes()
    {
        var (client, _) = Build(new FakeProcessRunner().Respond("pair", exitCode: -1, timedOut: true));

        var result = await client.PairAsync("192.168.1.25", 37123, "654321", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Un_mdns_en_echec_rend_une_liste_vide_plutot_qu_une_exception()
    {
        // Le mDNS est souvent bloqué par le réseau : ce n'est pas une panne.
        var (client, _) = Build(new FakeProcessRunner()
            .Respond("mdns services", standardError: "ERROR: mdns daemon unavailable", exitCode: 1));

        Assert.Empty(await client.ListMdnsServicesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Une_connexion_deja_etablie_n_est_pas_traitee_comme_une_erreur()
    {
        var (client, _) = Build(new FakeProcessRunner()
            .Respond("connect", "already connected to 192.168.1.25:37845"));

        var result = await client.ConnectAsync("192.168.1.25", 37845, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.AlreadyConnected);
    }

    [Fact]
    public async Task Une_annulation_remonte_sans_etre_transformee_en_erreur_adb()
    {
        var (client, _) = Build(new FakeProcessRunner().Respond("devices"));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ListDevicesAsync(true, source.Token));
    }
}

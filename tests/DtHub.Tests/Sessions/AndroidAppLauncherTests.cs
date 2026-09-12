using DtHub.Core.Adb;
using DtHub.Core.Dofus;
using DtHub.Core.Sessions;
using DtHub.Core.Users;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Sessions;

/// <summary>
/// Le lancement du jeu sur un profil Android : ce que le produit fait, et qui
/// n'avait aucune épreuve. Les épreuves de scrcpy passaient par un lanceur
/// simulé, donc celui-ci n'était jamais exercé.
///
/// Tout tourne sur le client ADB simulé, qui répond par du texte : on éprouve
/// donc aussi la lecture de ce texte, qui est là où se prennent les décisions.
/// </summary>
public class AndroidAppLauncherTests
{
    private const string Serial = "192.168.1.25:5555";
    private const string Package = "com.ankama.dofustouch";
    private const string Component = "com.ankama.dofustouch/.MainActivity";

    /// <summary>
    /// Sortie relevée sur le téléphone de référence, un Xiaomi 13T. Les
    /// drapeaux comptent : « 10 » ferait un profil secondaire, qui ne peut pas
    /// porter de fenêtre, et le lancement serait refusé avant d'être tenté.
    /// </summary>
    private static readonly string DeuxProfils = string.Join(
        '\n',
        "Users:",
        "\tUserInfo{0:Alice Martin:4c13} running",
        "\tUserInfo{999:XSpace:801010} running");

    private static AndroidAppLauncher Build(FakeAdbClient adb)
    {
        var users = new AndroidUserService(adb);
        return new AndroidAppLauncher(adb, users, new DofusInstanceService(adb, users));
    }

    [Fact]
    public async Task Le_lancement_porte_le_profil_et_l_afficheur()
    {
        // L'exigence centrale du produit : une instance par profil, chacune sur
        // son propre afficheur virtuel.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Starting: Intent { cmp=" + Component + " }\nStatus: ok");

        var result = await Build(adb).LaunchAsync(Serial, 999, Package, Component, 42);

        Assert.True(result.Succeeded);
        Assert.Contains("am start --user 999 --display 42 -n " + Component, adb.ShellCalls);
    }

    [Fact]
    public async Task Sans_afficheur_la_commande_n_en_nomme_aucun()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Status: ok");

        await Build(adb).LaunchAsync(Serial, 0, Package, Component, displayId: null);

        Assert.Contains("am start --user 0 -n " + Component, adb.ShellCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(11)]
    [InlineData(1234)]
    public async Task N_importe_quel_identifiant_de_profil_passe_tel_quel(int userId)
    {
        // Un profil cloné n'est pas toujours 999, et le README en fait une
        // promesse. Le nombre est écrit en culture invariante : sur un poste
        // dont la région emploie un séparateur, un identifiant à quatre
        // chiffres deviendrait « 1 234 ».
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Status: ok");

        await Build(adb).LaunchAsync(Serial, userId, Package, Component, null);

        Assert.Contains(
            adb.ShellCalls,
            c => c.Contains($"--user {userId}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_sortie_qui_porte_Error_est_un_echec_malgre_le_code_zero()
    {
        // « am start » rend zéro même lorsqu'il échoue : c'est la sortie qui
        // fait foi, et c'est cette lecture qui décide d'ouvrir une fenêtre ou
        // de la refuser.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm resolve-activity", string.Empty)
            .WithShell("am start", "Error: Activity class does not exist.");

        var result = await Build(adb).LaunchAsync(Serial, 0, Package, Component, null);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.UserMessage));
    }

    [Fact]
    public async Task Une_permission_refusee_ne_se_confond_pas_avec_un_jeu_absent()
    {
        // Supposer « application absente » sur n'importe quel échec envoyait
        // réinstaller un jeu bien présent.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm path", "package:/data/app/base.apk")
            .WithShell("pm resolve-activity", string.Empty)
            .WithShell("am start", "java.lang.SecurityException: Permission Denial: starting Intent");

        var result = await Build(adb).LaunchAsync(Serial, 0, Package, Component, null);

        Assert.False(result.Succeeded);
        Assert.Contains("Permission Denial", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sans_composant_connu_le_jeu_est_declare_absent_du_profil()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm resolve-activity", string.Empty);

        var result = await Build(adb).LaunchAsync(Serial, 999, Package, knownComponent: null, null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Un_arret_force_nomme_le_profil_et_le_paquet()
    {
        var adb = new FakeAdbClient().WithShell("am force-stop", string.Empty);

        Assert.True(await Build(adb).ForceStopAsync(Serial, 10, Package));

        Assert.Contains("am force-stop --user 10 " + Package, adb.ShellCalls);
    }

    [Fact]
    public async Task Un_arret_qui_n_atteint_pas_l_appareil_se_declare_manque()
    {
        // **Ce que l'épreuve d'avant affirmait à tort.** Elle lisait une faute
        // comme « l'application n'était peut-être pas ouverte », et faisait
        // donc passer pour un succès un ordre qui n'était jamais parti.
        //
        // Mesuré sur les deux téléphones, platform-tools 37.0.1 :
        // « am force-stop » rend 0 sur un paquet arrêté, et jusque sur un
        // paquet qui n'existe pas. Il ne rend 1 que sur « device offline » ou
        // « device not found », c'est-à-dire quand le téléphone n'a rien reçu.
        var adb = new FakeAdbClient().FailShell("am force-stop", AdbErrorKind.DeviceOffline);

        Assert.False(await Build(adb).ForceStopAsync(Serial, 10, Package));
    }

    [Fact]
    public async Task Un_serial_vide_est_refuse_avant_toute_commande()
    {
        var adb = new FakeAdbClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Build(adb).LaunchAsync(" ", 0, Package, Component, null));

        Assert.Empty(adb.ShellCalls);
    }
}

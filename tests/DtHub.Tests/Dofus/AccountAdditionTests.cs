using DtHub.Core.Adb;
using DtHub.Core.Dofus;
using DtHub.Core.Users;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Dofus;

/// <summary>
/// Adding an account: a fresh Android profile, the game inside it, the profile
/// started. The outputs are those recorded on the reference phone.
/// </summary>
public sealed class AccountAdditionTests
{
    private const string DeuxProfils = """
        Users:
        	UserInfo{0:Alice Martin:4c13} running
        	UserInfo{999:XSpace:801010} running
        """;

    /// <summary>
    /// A phone where no slot is taken. This is the ordinary case of a new
    /// device, and the one where the cloned profile can be created.
    /// </summary>
    private const string AucunProfil = """
        Users:
        	UserInfo{0:Alice Martin:4c13} running
        """;

    private static DofusInstanceService Service(FakeAdbClient adb) =>
        new(adb, new AndroidUserService(adb));

    private static FakeAdbClient Sain() =>
        new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm get-max-users", "Maximum supported users: 4")
            .WithShell("pm create-user", "Success: created user id 10")
            .WithShell("install-existing", "Package com.ankama.dofustouch installed for user: 10")
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("am start-user", "Success: user started");

    [Fact]
    public async Task Un_compte_s_ajoute_et_le_jeu_l_attend()
    {
        var adb = Sain();

        var result = await Service(adb).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(10, result.UserId);

        // The profile is started: an application does not open on a stopped
        // profile, and the user just asked for an account in order to use it.
        Assert.Contains(adb.ShellCalls, c => c.Contains("am start-user", StringComparison.Ordinal));

        // And the game is not downloaded again: it is the application already
        // present that is granted to the new profile, signed by its publisher.
        Assert.Contains(adb.ShellCalls, c => c.Contains("install-existing", StringComparison.Ordinal));
    }

    /// <summary>
    /// The message says what awaits the user: a fresh profile, so a game that
    /// asks for everything again. Staying silent about it would make a long
    /// wait look like a failure.
    /// </summary>
    [Fact]
    public async Task La_reussite_previent_que_le_profil_est_neuf()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", AucunProfil)
            .WithShell("pm get-max-users", "Maximum supported users: 4")
            .WithShell("pm create-user", "Success: created user id 10")
            .WithShell("install-existing", "Package com.ankama.dofustouch installed for user: 10")
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("am start-user", "Success: user started");

        var result = await Service(adb).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.Contains("Troisième", result.Message, StringComparison.Ordinal);
        Assert.Contains("neuf", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Le_profil_clone_est_demande_en_premier()
    {
        // This is the one used by OEM skins to duplicate an application: the
        // phone installs almost nothing on it, and its icons carry no mark.
        // The managed profile, by contrast, arrives pre-loaded.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", AucunProfil)
            .WithShell("pm get-max-users", "Maximum supported users: 4")
            .WithShell("pm create-user", "Success: created user id 10")
            .WithShell("install-existing", "Package com.ankama.dofustouch installed for user: 10")
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("am start-user", "Success: user started");

        var result = await Service(adb).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);

        Assert.Contains(
            adb.ShellCalls,
            c => c.Contains("android.os.usertype.profile.CLONE", StringComparison.Ordinal));

        Assert.DoesNotContain(
            adb.ShellCalls,
            c => c.Contains("profile.MANAGED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sans_place_clonee_le_repli_professionnel_est_annonce()
    {
        // The cloned slot is taken by XSpace. The fallback works, but it
        // changes what will be seen on the home screen: it must not happen
        // silently.
        var result = await Service(Sain()).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("professionnel", result.Message, StringComparison.Ordinal);
        Assert.Contains("valise", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_telephone_plein_refuse_avant_d_essayer()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm get-max-users", "Maximum supported users: 2");

        var result = await Service(adb).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("2 profils", result.Message, StringComparison.Ordinal);

        // Nothing was attempted: the refusal comes from the lack of room, not
        // from a failure.
        Assert.DoesNotContain(adb.ShellCalls, c => c.Contains("create-user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_surcouche_qui_refuse_la_creation_se_dit_clairement()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm get-max-users", "Maximum supported users: 4")
            .WithShell("pm create-user", "Error: couldn't create User.");

        var result = await Service(adb).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("refusé", result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The profile exists but the game is not on it: it cannot be hidden, the
    /// profile will stay in the phone's list.
    /// </summary>
    [Fact]
    public async Task Un_jeu_absent_apres_installation_est_signale_avec_le_profil()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm get-max-users", "Maximum supported users: 4")
            .WithShell("pm create-user", "Success: created user id 10")
            .WithShell("install-existing", "Package com.ankama.dofustouch installed for user: 10")
            .WithShell("pm list packages", string.Empty);

        var result = await Service(adb).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(10, result.UserId);
        Assert.Contains("ne s'y trouve pas", result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A phone that does not state its limit must not block the addition: we
    /// try, and it is the one that decides.
    /// </summary>
    [Fact]
    public async Task Une_limite_inconnue_n_empeche_pas_d_essayer()
    {
        var adb = Sain().WithShell("pm get-max-users", "commande inconnue");

        var result = await Service(adb).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
    }

    [Theory]
    [InlineData("Success: created user id 10", 10)]
    [InlineData("Success: created user id 0", 0)]
    [InlineData("  Success: created user id 12  ", 12)]
    public void Le_profil_cree_se_lit_dans_la_reponse(string sortie, int attendu) =>
        Assert.Equal(attendu, AndroidUserParser.ParseCreatedUserId(sortie));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Error: couldn't create User.")]
    [InlineData("Success")]
    public void Une_reponse_sans_identifiant_ne_cree_rien(string? sortie) =>
        Assert.Null(AndroidUserParser.ParseCreatedUserId(sortie));

    [Theory]
    [InlineData("Maximum supported users: 4", 4)]
    [InlineData("Maximum supported users: 1", 1)]
    public void La_limite_de_profils_se_lit(string sortie, int attendu) =>
        Assert.Equal(attendu, AndroidUserParser.ParseMaxUsers(sortie));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown command")]
    public void Une_limite_illisible_rend_rien(string? sortie) =>
        Assert.Null(AndroidUserParser.ParseMaxUsers(sortie));

    /// <summary>
    /// Recorded on the reference phone: the managed profile carries the 0x20
    /// flag in its flags, here 0x1030.
    /// </summary>
    private const string AvecProfilGere = """
        Users:
        	UserInfo{0:Alice Martin:4c13} running
        	UserInfo{10:Compte 3:1030} running
        	UserInfo{999:XSpace:801010} running
        """;

    [Fact]
    public async Task Un_second_profil_gere_est_refuse_quand_le_premier_porte_le_jeu()
    {
        // Android only accepts one. As long as it is in use, there is nothing
        // to do.
        //
        // The client is built by hand: the fake keeps the first matching rule,
        // so adding a second one on "pm list users" replaces nothing.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", AvecProfilGere)
            .WithShell("pm get-max-users", "Maximum supported users: 4")
            .WithShell("pm list packages", "package:com.ankama.dofustouch");

        var ajout = await Service(adb).AddAccountAsync("SERIE1", "Compte 4");

        Assert.False(ajout.Succeeded);
        Assert.DoesNotContain("pm create-user", adb.ShellCalls);
    }

    [Fact]
    public async Task Un_profil_gere_sans_jeu_est_repris_au_lieu_d_etre_refuse()
    {
        // The case recorded on the machine: the one slot Android grants was
        // occupied by a profile whose game had disappeared. Refusing left no
        // recourse, since nothing indicated that this was the one to repair.
        var adb = new FakeAdbClient()
            .WithShellChanging(
                "list packages --user 10", string.Empty, "package:com.ankama.dofustouch")
            .WithShell("pm list users", AvecProfilGere)
            .WithShell("pm get-max-users", "Maximum supported users: 4")
            .WithShell("install-existing", "Package com.ankama.dofustouch installed for user: 10")
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("am start-user", "Success: user started");

        var ajout = await Service(adb).AddAccountAsync("SERIE1", "Compte 4");

        Assert.True(ajout.Succeeded, ajout.Message);
        Assert.Equal(10, ajout.UserId);

        // Reused, so not recreated: the slot is unique and it is already
        // taken.
        Assert.DoesNotContain(adb.ShellCalls, c => c.Contains("create-user", StringComparison.Ordinal));

        // And filled: the game placed, the profile started.
        Assert.Contains(adb.ShellCalls, c => c.Contains("install-existing", StringComparison.Ordinal));
        Assert.Contains(adb.ShellCalls, c => c.Contains("start-user", StringComparison.Ordinal));
    }
}

using DtHub.Core.Adb;
using DtHub.Core.Users;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Users;

public class AndroidUserServiceTests
{
    private const string PmListUsers = """
        Users:
        	UserInfo{0:Propriétaire:c13} running
        	UserInfo{999:Applications dupliquées:1030} running
        """;

    private const string DumpsysUser = """
        Users:
          UserInfo{0:Propriétaire:c13} serialNo=0
            Type: android.os.usertype.full.SYSTEM
          UserInfo{999:Applications dupliquées:1030} serialNo=999
            Type: android.os.usertype.profile.CLONE
        """;

    [Fact]
    public async Task Les_utilisateurs_du_telephone_sont_listes()
    {
        var adb = new FakeAdbClient().WithShell("pm list users", PmListUsers).WithShell("dumpsys user", DumpsysUser);

        var users = await new AndroidUserService(adb).GetUsersAsync("USB0001", false, CancellationToken.None);

        Assert.Equal([0, 999], users.Select(u => u.Id));
        Assert.Equal(AndroidUserType.CloneProfile, users.Single(u => u.Id == 999).Type);
    }

    [Fact]
    public async Task La_liste_n_est_relue_qu_une_fois_sans_demande_explicite()
    {
        var adb = new FakeAdbClient().WithShell("pm list users", PmListUsers).WithShell("dumpsys user", DumpsysUser);
        var service = new AndroidUserService(adb);

        await service.GetUsersAsync("USB0001", false, CancellationToken.None);
        await service.GetUsersAsync("USB0001", false, CancellationToken.None);

        Assert.Single(adb.ShellCalls, c => c.StartsWith("pm list users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_rafraichissement_demande_relit_la_liste()
    {
        var adb = new FakeAdbClient().WithShell("pm list users", PmListUsers).WithShell("dumpsys user", DumpsysUser);
        var service = new AndroidUserService(adb);

        await service.GetUsersAsync("USB0001", false, CancellationToken.None);
        await service.GetUsersAsync("USB0001", true, CancellationToken.None);

        Assert.Equal(2, adb.ShellCalls.Count(c => c.StartsWith("pm list users", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Dumpsys_n_est_pas_appele_quand_aucun_type_n_est_ambigu()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", "Users:\n\tUserInfo{0:Propriétaire:c13} running\n");

        await new AndroidUserService(adb).GetUsersAsync("USB0001", false, CancellationToken.None);

        Assert.DoesNotContain(adb.ShellCalls, c => c.StartsWith("dumpsys", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_dumpsys_indisponible_ne_fait_pas_perdre_la_liste()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", PmListUsers)
            .FailShell("dumpsys user");

        var users = await new AndroidUserService(adb).GetUsersAsync("USB0001", false, CancellationToken.None);

        Assert.Equal(2, users.Count);
        Assert.Equal(AndroidUserType.ManagedProfile, users.Single(u => u.Id == 999).Type);
    }

    [Fact]
    public async Task Un_telephone_qui_refuse_la_commande_garde_son_utilisateur_principal()
    {
        var adb = new FakeAdbClient().FailShell("pm list users", AdbErrorKind.DeviceUnauthorized);

        var users = await new AndroidUserService(adb).GetUsersAsync("USB0001", false, CancellationToken.None);

        var user = Assert.Single(users);
        Assert.Equal(0, user.Id);
        Assert.True(user.IsPrimary);
    }

    [Fact]
    public async Task Une_sortie_illisible_donne_aussi_l_utilisateur_principal()
    {
        var adb = new FakeAdbClient().WithShell("pm list users", "Error: no such command");

        var users = await new AndroidUserService(adb).GetUsersAsync("USB0001", false, CancellationToken.None);

        Assert.Equal(0, Assert.Single(users).Id);
    }

    [Fact]
    public async Task Un_utilisateur_est_retrouve_par_son_identifiant()
    {
        var adb = new FakeAdbClient().WithShell("pm list users", PmListUsers).WithShell("dumpsys user", DumpsysUser);
        var service = new AndroidUserService(adb);

        Assert.Equal(999, (await service.FindAsync("USB0001", 999, CancellationToken.None))!.Id);
        Assert.Null(await service.FindAsync("USB0001", 12345, CancellationToken.None));
    }

    [Fact]
    public async Task Demarrer_un_utilisateur_arrete_est_signale_et_vide_le_cache()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", PmListUsers)
            .WithShell("dumpsys user", DumpsysUser)
            .WithShell("am start-user", "Success: user started");

        var service = new AndroidUserService(adb);
        await service.GetUsersAsync("USB0001", false, CancellationToken.None);

        Assert.True(await service.TryStartUserAsync("USB0001", 999, CancellationToken.None));

        await service.GetUsersAsync("USB0001", false, CancellationToken.None);
        Assert.Equal(2, adb.ShellCalls.Count(c => c.StartsWith("pm list users", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Un_demarrage_refuse_rend_faux_sans_lever()
    {
        var adb = new FakeAdbClient().WithShell("am start-user", "Error: could not start user 999");

        Assert.False(await new AndroidUserService(adb).TryStartUserAsync("USB0001", 999, CancellationToken.None));
    }

    [Fact]
    public async Task Les_caches_de_deux_telephones_sont_independants()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", "Users:\n\tUserInfo{0:Propriétaire:c13} running\n");

        var service = new AndroidUserService(adb);

        await service.GetUsersAsync("USB0001", false, CancellationToken.None);
        await service.GetUsersAsync("USB0002", false, CancellationToken.None);

        Assert.Equal(2, adb.ShellCalls.Count(c => c.StartsWith("pm list users", StringComparison.Ordinal)));
    }
}

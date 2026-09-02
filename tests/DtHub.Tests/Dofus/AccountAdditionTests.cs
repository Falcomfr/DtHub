using DtHub.Core.Adb;
using DtHub.Core.Dofus;
using DtHub.Core.Users;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Dofus;

/// <summary>
/// L'ajout d'un compte : un profil Android neuf, le jeu dedans, le profil
/// démarré. Les sorties sont celles relevées sur le téléphone de référence.
/// </summary>
public sealed class AccountAdditionTests
{
    private const string DeuxProfils = """
        Users:
        	UserInfo{0:Alice Martin:4c13} running
        	UserInfo{999:XSpace:801010} running
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

        // Le profil est démarré : une application ne s'ouvre pas sur un profil
        // arrêté, et l'utilisateur vient de demander un compte pour s'en servir.
        Assert.Contains(adb.ShellCalls, c => c.Contains("am start-user", StringComparison.Ordinal));

        // Et le jeu n'est pas retéléchargé : c'est l'application déjà présente
        // qui est rendue au nouveau profil, signée par son éditeur.
        Assert.Contains(adb.ShellCalls, c => c.Contains("install-existing", StringComparison.Ordinal));
    }

    /// <summary>
    /// Le message dit ce qui attend l'utilisateur : un profil neuf, donc un jeu
    /// qui redemande tout. Le taire ferait passer une longue attente pour une
    /// panne.
    /// </summary>
    [Fact]
    public async Task La_reussite_previent_que_le_profil_est_neuf()
    {
        var result = await Service(Sain()).AddAccountAsync("USB0001", "Troisième", CancellationToken.None);

        Assert.Contains("Troisième", result.Message, StringComparison.Ordinal);
        Assert.Contains("neuf", result.Message, StringComparison.Ordinal);
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

        // Rien n'a été tenté : le refus vient de la place, pas d'un échec.
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
    /// Le profil existe mais le jeu n'y est pas : on ne peut pas le cacher, le
    /// profil restera dans la liste du téléphone.
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
    /// Un téléphone qui ne dit pas sa limite ne doit pas bloquer l'ajout : on
    /// tente, et c'est lui qui tranche.
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
}

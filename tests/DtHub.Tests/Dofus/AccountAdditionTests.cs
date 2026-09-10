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

    /// <summary>
    /// Un téléphone où aucune place n'est prise. C'est le cas ordinaire d'un
    /// appareil neuf, et celui où le profil cloné peut être créé.
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
        // C'est celui que les surcouches emploient pour dupliquer une
        // application : le téléphone n'y installe presque rien, et ses icônes
        // ne portent aucune marque. Le professionnel, lui, arrive garni.
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
        // La place clonée est prise par XSpace. Le repli marche, mais il change
        // ce qu'on verra sur l'écran d'accueil : il ne doit pas se faire en
        // silence.
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

    /// <summary>
    /// Relevé sur le téléphone de référence : le profil géré porte l'indicateur
    /// 0x20 dans ses drapeaux, ici 0x1030.
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
        // Android n'en accepte qu'un. Tant qu'il sert, il n'y a rien à faire.
        //
        // Le client est bâti à la main : le faux garde la première règle qui
        // correspond, donc en ajouter une seconde sur « pm list users » ne
        // remplace rien.
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
        // Le cas relevé sur le poste : l'unique place qu'Android accorde était
        // occupée par un profil dont le jeu avait disparu. Refuser laissait
        // sans recours, puisque rien ne disait qu'il fallait réparer celui-là.
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

        // Repris, donc pas recréé : la place est unique et elle est déjà prise.
        Assert.DoesNotContain(adb.ShellCalls, c => c.Contains("create-user", StringComparison.Ordinal));

        // Et rempli : le jeu posé, le profil démarré.
        Assert.Contains(adb.ShellCalls, c => c.Contains("install-existing", StringComparison.Ordinal));
        Assert.Contains(adb.ShellCalls, c => c.Contains("start-user", StringComparison.Ordinal));
    }
}

using DtHub.Core.Users;

namespace DtHub.Tests.Users;

public class AndroidUserParserTests
{
    /// <summary>Sortie relevée sur un Xiaomi avec applications dupliquées et second espace.</summary>
    private const string XiaomiOutput = """
        Users:
        	UserInfo{0:Propriétaire:c13} running
        	UserInfo{999:Applications dupliquées:1030} running
        	UserInfo{10:Second espace:10}
        """;

    [Fact]
    public void Une_sortie_vide_ne_leve_pas()
    {
        Assert.Empty(AndroidUserParser.Parse(null));
        Assert.Empty(AndroidUserParser.Parse("Users:\n"));
        Assert.Empty(AndroidUserParser.Parse("Error: no such command\n"));
    }

    [Fact]
    public void Les_trois_utilisateurs_sont_lus_et_tries_par_identifiant()
    {
        var users = AndroidUserParser.Parse(XiaomiOutput);

        Assert.Equal([0, 10, 999], users.Select(u => u.Id));
    }

    [Fact]
    public void L_utilisateur_zero_est_le_principal()
    {
        var user = AndroidUserParser.Parse(XiaomiOutput).Single(u => u.Id == 0);

        Assert.Equal(AndroidUserType.Primary, user.Type);
        Assert.True(user.IsPrimary);
        Assert.True(user.IsRunning);
    }

    [Fact]
    public void Un_profil_gere_est_reconnu_par_ses_drapeaux_et_non_par_son_identifiant()
    {
        var user = AndroidUserParser.Parse(XiaomiOutput).Single(u => u.Id == 999);

        Assert.Equal(AndroidUserType.ManagedProfile, user.Type);
    }

    [Fact]
    public void Le_meme_type_de_profil_est_reconnu_sur_un_autre_identifiant()
    {
        // Garde-fou contre l'hypothèse « le clone vaut toujours 999 ».
        var user = AndroidUserParser.ParseLine("UserInfo{42:Applications dupliquées:1030} running");

        Assert.NotNull(user);
        Assert.Equal(42, user.Id);
        Assert.Equal(AndroidUserType.ManagedProfile, user.Type);
    }

    [Fact]
    public void Un_profil_de_clonage_sans_drapeau_de_gestion_est_reconnu_comme_clone()
    {
        var user = AndroidUserParser.ParseLine("UserInfo{11:Clone:1010} running");

        Assert.NotNull(user);
        Assert.Equal(AndroidUserType.CloneProfile, user.Type);
        Assert.Equal("Clone", user.TypeLabel);
    }

    [Fact]
    public void Un_utilisateur_secondaire_complet_n_est_ni_clone_ni_profil_pro()
    {
        var user = AndroidUserParser.Parse(XiaomiOutput).Single(u => u.Id == 10);

        Assert.Equal(AndroidUserType.Secondary, user.Type);
        Assert.False(user.IsRunning);
    }

    [Theory]
    [InlineData("UserInfo{11:Invité:4}", AndroidUserType.Guest)]
    [InlineData("UserInfo{12:Restreint:8}", AndroidUserType.Restricted)]
    [InlineData("UserInfo{0:Owner:13}", AndroidUserType.Primary)]
    [InlineData("UserInfo{13:Secondaire:410}", AndroidUserType.Secondary)]
    public void Chaque_famille_de_drapeaux_est_classee(string line, AndroidUserType expected)
    {
        Assert.Equal(expected, AndroidUserParser.ParseLine(line)!.Type);
    }

    [Fact]
    public void Un_utilisateur_arrete_est_signale_comme_tel()
    {
        var running = AndroidUserParser.ParseLine("UserInfo{999:Clone:1030} running");
        var stopped = AndroidUserParser.ParseLine("UserInfo{999:Clone:1030}");

        Assert.True(running!.IsRunning);
        Assert.False(stopped!.IsRunning);
    }

    [Fact]
    public void Un_nom_contenant_un_deux_points_reste_entier()
    {
        var user = AndroidUserParser.ParseLine("UserInfo{7:Travail: équipe A:30} running");

        Assert.NotNull(user);
        Assert.Equal(7, user.Id);
        Assert.Equal("Travail: équipe A", user.Name);
        Assert.Equal(AndroidUserType.ManagedProfile, user.Type);
    }

    [Fact]
    public void Les_drapeaux_sont_lus_en_hexadecimal_avec_ou_sans_prefixe()
    {
        var withPrefix = AndroidUserParser.ParseLine("UserInfo{5:Clone:0x1030}");
        var withoutPrefix = AndroidUserParser.ParseLine("UserInfo{5:Clone:1030}");

        Assert.Equal(withoutPrefix!.Flags, withPrefix!.Flags);
        Assert.Equal(0x1030, withPrefix.Flags);
    }

    [Fact]
    public void Une_ligne_malformee_est_ignoree_sans_priver_des_autres()
    {
        const string output = """
            Users:
            	UserInfo{0:Propriétaire:c13} running
            	UserInfo{ceci-est-casse}
            	UserInfo{pasunnombre:Nom:13}
            	UserInfo{10:Clone:1030} running
            """;

        var users = AndroidUserParser.Parse(output);

        Assert.Equal([0, 10], users.Select(u => u.Id));
    }

    [Fact]
    public void Un_meme_identifiant_repete_n_apparait_qu_une_fois()
    {
        const string output = """
            Users:
            	UserInfo{0:Propriétaire:c13} running
            	UserInfo{0:Propriétaire:c13} running
            """;

        Assert.Single(AndroidUserParser.Parse(output));
    }

    [Fact]
    public void Le_nom_donne_par_le_telephone_est_celui_qui_est_affiche()
    {
        var clone = AndroidUserParser.ParseLine("UserInfo{999:Applications dupliquées:1030}");
        var owner = AndroidUserParser.ParseLine("UserInfo{0:Propriétaire:c13}");
        var unnamed = AndroidUserParser.ParseLine("UserInfo{12::1030}");

        Assert.Equal("Applications dupliquées", clone!.DisplayName);

        // L'utilisateur principal garde un libellé stable, quel que soit le
        // nom que le téléphone lui donne selon sa langue.
        Assert.Equal("Principal", owner!.DisplayName);

        Assert.Equal("Profil géré 12", unnamed!.DisplayName);
    }

    [Fact]
    public void Dumpsys_affine_le_type_quand_les_drapeaux_sont_ambigus()
    {
        // Sur cette sortie, 999 et 10 portent les mêmes drapeaux de profil
        // géré ; seul dumpsys sait que l'un est un clone.
        var users = AndroidUserParser.Parse("""
            Users:
            	UserInfo{0:Propriétaire:c13} running
            	UserInfo{10:Profil professionnel:1030} running
            	UserInfo{999:Applications dupliquées:1030} running
            """);

        var refined = AndroidUserParser.ApplyUserTypes(users, """
            Users:
              UserInfo{0:Propriétaire:c13} serialNo=0 isPrimary=true
                Type: android.os.usertype.full.SYSTEM
              UserInfo{10:Profil professionnel:1030} serialNo=10
                Type: android.os.usertype.profile.MANAGED
              UserInfo{999:Applications dupliquées:1030} serialNo=999
                Type: android.os.usertype.profile.CLONE
            """);

        Assert.Equal(AndroidUserType.Primary, refined.Single(u => u.Id == 0).Type);
        Assert.Equal(AndroidUserType.ManagedProfile, refined.Single(u => u.Id == 10).Type);
        Assert.Equal(AndroidUserType.CloneProfile, refined.Single(u => u.Id == 999).Type);
    }

    [Fact]
    public void Sans_sortie_dumpsys_le_classement_par_drapeaux_est_conserve()
    {
        var users = AndroidUserParser.Parse(XiaomiOutput);

        Assert.Same(users, AndroidUserParser.ApplyUserTypes(users, null));
        Assert.Same(users, AndroidUserParser.ApplyUserTypes(users, "dumpsys: command not found"));
    }

    [Fact]
    public void Un_utilisateur_absent_de_dumpsys_garde_son_classement()
    {
        var users = AndroidUserParser.Parse(XiaomiOutput);

        var refined = AndroidUserParser.ApplyUserTypes(users, """
            Users:
              UserInfo{0:Propriétaire:c13} serialNo=0
                Type: android.os.usertype.full.SYSTEM
            """);

        Assert.Equal(AndroidUserType.ManagedProfile, refined.Single(u => u.Id == 999).Type);
    }

    [Fact]
    public void Des_drapeaux_illisibles_ne_font_pas_perdre_l_utilisateur()
    {
        var user = AndroidUserParser.ParseLine("UserInfo{3:Bizarre:zzzz} running");

        Assert.NotNull(user);
        Assert.Equal(3, user.Id);
        Assert.Equal(0, user.Flags);
        Assert.Equal(AndroidUserType.Secondary, user.Type);
    }

    [Fact]
    public void Un_profil_en_pause_est_reconnu()
    {
        // 0x10b0 : profil géré, initialisé, et le drapeau 0x80 de la pause.
        // C'est l'état d'un profil professionnel dont l'interrupteur est
        // éteint, et celui que rendent Shelter et Island au repos.
        var user = AndroidUserParser.ParseLine("UserInfo{10:Travail:10b0} running");

        Assert.NotNull(user);
        Assert.True(user.IsPaused);
        Assert.Equal(AndroidUserType.ManagedProfile, user.Type);
    }

    [Fact]
    public void Un_profil_actif_n_est_pas_dit_en_pause()
    {
        // Relevé réel : le profil géré créé sur le téléphone de référence
        // porte 0x1030, sans le drapeau de pause.
        var user = AndroidUserParser.ParseLine("UserInfo{15:Travail:1030} running");

        Assert.NotNull(user);
        Assert.False(user.IsPaused);
    }

    [Fact]
    public void La_pause_ne_se_confond_pas_avec_l_arret()
    {
        // Un profil peut tourner et être en pause : ce sont deux états
        // distincts, et les confondre ferait tenter un lancement voué à
        // l'échec.
        var user = AndroidUserParser.ParseLine("UserInfo{10:Travail:10b0} running");

        Assert.NotNull(user);
        Assert.True(user.IsRunning);
        Assert.True(user.IsPaused);
    }
}

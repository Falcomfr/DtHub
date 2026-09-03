using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class LaunchProfilesTests
{
    private static StoredInstance Compte(string device, int user, string? nom = null) => new()
    {
        DeviceId = device,
        UserId = user,
        PackageName = "com.ankama.dofustouch",
        UserName = $"Profil {user}",
        CustomName = nom,
    };

    private static readonly StoredInstance Principal = Compte("phone", 0, "Principal");
    private static readonly StoredInstance XSpace = Compte("phone", 999, "XSpace");
    private static readonly StoredInstance SansNom = Compte("phone", 15);

    private static StoredLaunchProfile Profil(string nom, params StoredInstance[] comptes) => new()
    {
        Name = nom,
        InstanceKeys = [.. comptes.Select(c => c.Key)],
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Un_nom_vide_n_en_est_pas_un(string? nom)
    {
        Assert.Null(LaunchProfiles.Normalize(nom));
    }

    [Fact]
    public void Les_espaces_de_bord_partent()
    {
        // « Duo » et « Duo  » seraient deux profils qu'on ne saurait pas
        // distinguer dans la liste.
        Assert.Equal("Duo", LaunchProfiles.Normalize("  Duo  "));
    }

    [Fact]
    public void Un_nom_trop_long_est_coupe_plutot_que_refuse()
    {
        var coupe = LaunchProfiles.Normalize(new string('a', 80));

        Assert.NotNull(coupe);
        Assert.Equal(LaunchProfiles.MaxNameLength, coupe.Length);
    }

    [Fact]
    public void La_recherche_ignore_la_casse_et_les_espaces()
    {
        List<StoredLaunchProfile> profils = [Profil("Duo pêche", Principal, XSpace)];

        Assert.NotNull(LaunchProfiles.Find(profils, "duo pêche"));
        Assert.NotNull(LaunchProfiles.Find(profils, "  DUO PÊCHE "));
        Assert.Null(LaunchProfiles.Find(profils, "duo"));
        Assert.Null(LaunchProfiles.Find(profils, null));
        Assert.Null(LaunchProfiles.Find(null, "Duo pêche"));
    }

    [Fact]
    public void Les_comptes_du_profil_sont_rendus()
    {
        List<StoredLaunchProfile> profils = [Profil("Duo", Principal, XSpace)];

        Assert.Equal(
            [Principal.Key, XSpace.Key],
            LaunchProfiles.KeysFor(profils, "Duo", [Principal, XSpace]));
    }

    [Fact]
    public void Un_compte_disparu_du_telephone_est_ecarte()
    {
        // Le profil garde sa raison d'être : les comptes restants s'ouvrent,
        // au lieu de tout refuser pour un profil Android supprimé.
        List<StoredLaunchProfile> profils = [Profil("Duo", Principal, XSpace)];

        Assert.Equal([Principal.Key], LaunchProfiles.KeysFor(profils, "Duo", [Principal]));
    }

    [Fact]
    public void Un_profil_inconnu_ne_rend_aucun_compte()
    {
        Assert.Empty(LaunchProfiles.KeysFor([], "Duo", [Principal]));
        Assert.Empty(LaunchProfiles.KeysFor(null, "Duo", [Principal]));
    }

    [Fact]
    public void Une_cle_repetee_ne_compte_qu_une_fois()
    {
        List<StoredLaunchProfile> profils =
            [new() { Name = "Duo", InstanceKeys = [Principal.Key, Principal.Key] }];

        Assert.Equal([Principal.Key], LaunchProfiles.KeysFor(profils, "Duo", [Principal]));
    }

    [Fact]
    public void Le_resume_nomme_les_comptes_tant_qu_ils_tiennent()
    {
        var phrase = LaunchProfiles.Describe(Profil("Duo", XSpace, Principal), [XSpace, Principal]);

        Assert.Equal("XSpace + Principal", phrase);
    }

    [Fact]
    public void Le_resume_compte_au_dela_de_trois()
    {
        var quatre = new[] { Compte("p", 1), Compte("p", 2), Compte("p", 3), Compte("p", 4) };

        Assert.Equal("4 comptes", LaunchProfiles.Describe(Profil("Tout", quatre), quatre));
    }

    [Fact]
    public void Le_resume_prend_le_nom_du_profil_Android_a_defaut()
    {
        Assert.Equal("Profil 15", LaunchProfiles.Describe(Profil("Seul", SansNom), [SansNom]));
    }

    [Fact]
    public void Un_profil_vide_ou_perime_le_dit()
    {
        Assert.Equal("aucun compte", LaunchProfiles.Describe(Profil("Vide"), [Principal]));
        Assert.Equal("aucun compte", LaunchProfiles.Describe(null, [Principal]));

        // Toutes ses clés désignent des comptes disparus.
        Assert.Equal("aucun compte connu", LaunchProfiles.Describe(Profil("Duo", XSpace), [Principal]));
    }

    [Fact]
    public void L_annonce_dit_les_comptes_la_qualite_la_distance_et_le_son()
    {
        var phrase = LaunchProfiles.Announce(2, StreamQuality.Maximum, GameZoom.Wide, 0, audio: true);

        Assert.Equal(
            "Ce profil retiendra les 2 comptes ouverts, la position et la taille de leurs "
            + "fenêtres, la qualité haute, la distance éloignée et le son renvoyé sur le PC.",
            phrase);
    }

    [Fact]
    public void L_annonce_dit_le_son_coupe()
    {
        Assert.EndsWith(
            "et le son coupé.",
            LaunchProfiles.Announce(1, StreamQuality.Medium, GameZoom.Normal, 0, audio: false),
            StringComparison.Ordinal);
    }

    [Fact]
    public void L_annonce_accorde_le_compte_unique()
    {
        var phrase = LaunchProfiles.Announce(1, StreamQuality.Medium, GameZoom.Normal, 0, audio: false);

        Assert.StartsWith("Ce profil retiendra le compte ouvert,", phrase, StringComparison.Ordinal);
        Assert.Contains("la qualité moyenne, la distance normale", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void L_annonce_ne_parle_des_onglets_que_s_il_y_en_a()
    {
        Assert.DoesNotContain(
            "onglets",
            LaunchProfiles.Announce(2, StreamQuality.Low, GameZoom.Close, 0, audio: false),
            StringComparison.Ordinal);

        Assert.EndsWith(
            "Le compte logé dans le cadre à onglets y retournera, cadre à sa place.",
            LaunchProfiles.Announce(2, StreamQuality.Low, GameZoom.Close, 1, audio: false),
            StringComparison.Ordinal);

        Assert.EndsWith(
            "Les 2 comptes logés dans le cadre à onglets y retourneront, cadre à sa place.",
            LaunchProfiles.Announce(2, StreamQuality.Low, GameZoom.Close, 2, audio: false),
            StringComparison.Ordinal);
    }
}

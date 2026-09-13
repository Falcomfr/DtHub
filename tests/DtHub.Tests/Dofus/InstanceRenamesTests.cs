using DtHub.Core.Dofus;

namespace DtHub.Tests.Dofus;

public class InstanceRenamesTests
{
    private static OpenInstance Ouvert(string key, string userName, string shown) =>
        new(key, userName, shown);

    [Fact]
    public void Un_compte_renomme_doit_etre_reecrit()
    {
        var pending = InstanceRenames.Pending(
            [Ouvert("tel|0|jeu", "Principal", "Principal")],
            _ => "Cra, Enu");

        Assert.Equal("Cra, Enu", pending["tel|0|jeu"]);
    }

    [Fact]
    public void Un_compte_dont_le_nom_n_a_pas_bouge_n_est_pas_touche()
    {
        // The settings event fires on every write, including for the
        // geometry of a window being dragged. Rewriting the titles
        // every time would be noise across every open window.
        var pending = InstanceRenames.Pending(
            [Ouvert("tel|0|jeu", "Principal", "Cra, Enu")],
            _ => "Cra, Enu");

        Assert.Empty(pending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Effacer_le_nom_rend_celui_du_profil_android(string? efface)
    {
        // The trap in this fix: an emptied name does not leave a tab
        // without a title; it falls back to the name the phone reports.
        var pending = InstanceRenames.Pending(
            [Ouvert("tel|0|jeu", "Principal", "Cra, Enu")],
            _ => efface);

        Assert.Equal("Principal", pending["tel|0|jeu"]);
    }

    [Fact]
    public void Les_espaces_autour_du_nom_ne_comptent_pas()
    {
        var pending = InstanceRenames.Pending(
            [Ouvert("tel|0|jeu", "Principal", "Cra, Enu")],
            _ => "  Cra, Enu  ");

        Assert.Empty(pending);
    }

    [Fact]
    public void Seuls_les_comptes_changes_sont_rendus()
    {
        var pending = InstanceRenames.Pending(
            [
                Ouvert("tel|0|jeu", "Principal", "Principal"),
                Ouvert("tel|999|jeu", "XSpace", "Eni, Iop"),
            ],
            key => key.Contains("|0|", StringComparison.Ordinal) ? "Cra, Enu" : "Eni, Iop");

        Assert.Equal("Cra, Enu", Assert.Single(pending).Value);
    }

    [Fact]
    public void La_meme_regle_que_le_nom_affiche_d_une_instance()
    {
        // Both must say the same thing: if they diverge, a renamed
        // account would show one name in the list and a different
        // one on its window.
        var instance = new DofusInstance
        {
            DeviceId = "tel",
            DeviceName = "Un téléphone",
            UserId = 0,
            UserName = "Principal",
            PackageName = "jeu",
            CustomName = "  Cra, Enu  ",
        };

        var pending = InstanceRenames.Pending(
            [Ouvert(instance.Key, instance.UserName, "Principal")],
            _ => instance.CustomName);

        Assert.Equal(instance.DisplayName, pending[instance.Key]);
    }

    [Fact]
    public void Aucune_fenetre_ouverte_ne_demande_rien()
    {
        Assert.Empty(InstanceRenames.Pending([], _ => "Cra, Enu"));
    }

    [Fact]
    public void Le_nom_a_montrer_est_celui_des_reglages_et_non_celui_du_lancement()
    {
        // A tab was seeded with the name frozen when scrcpy started.
        // Renaming a window that was open but not docked, then docking
        // it, showed the name from before the rename for the rest of
        // the session.
        Assert.Equal(
            "Cra, Enu",
            InstanceRenames.NameNow("tel|0|jeu", "Principal", _ => "Cra, Enu"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sans_nom_choisi_l_ancrage_rend_celui_du_profil_android(string? efface)
    {
        // The trap on the other side: a cleared name must fall back to
        // the Android profile, not leave the tab blank.
        Assert.Equal(
            "Principal",
            InstanceRenames.NameNow("tel|0|jeu", "Principal", _ => efface));
    }

    [Fact]
    public void Le_meme_calcul_sert_a_poser_un_onglet_et_a_le_renommer()
    {
        // The defect this type exists to catch came back through the
        // other door once already, because the label was computed in two
        // places. One function, used by both, is what keeps it from
        // happening a third time.
        var pending = InstanceRenames.Pending(
            [Ouvert("tel|0|jeu", "Principal", "Principal")],
            _ => "Cra, Enu");

        Assert.Equal(
            pending["tel|0|jeu"],
            InstanceRenames.NameNow("tel|0|jeu", "Principal", _ => "Cra, Enu"));
    }
}

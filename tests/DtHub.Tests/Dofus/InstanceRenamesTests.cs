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
        // L'évènement des réglages se lève à chaque écriture, y compris pour
        // la géométrie d'une fenêtre qu'on déplace. Réécrire les titres à
        // chaque fois serait du bruit sur toutes les fenêtres ouvertes.
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
        // Le piège de ce correctif : un nom vidé ne laisse pas un onglet sans
        // titre, il rend le nom que le téléphone rapporte.
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
        // Les deux doivent dire la même chose : si elles divergent, un compte
        // renommé porterait un nom dans la liste et un autre sur sa fenêtre.
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
}

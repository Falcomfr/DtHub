using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// A device's name appears only where the device changes. Two
/// consecutive instances of the same phone carry only one; a phone
/// split in two by an instance coming from elsewhere gets one header
/// per piece.
/// </summary>
public sealed class DeviceHeadersTests
{
    [Fact]
    public void La_premiere_instance_porte_toujours_un_en_tete()
    {
        Assert.Equal([true], DeviceHeaders.For(["A"]));
    }

    [Fact]
    public void Deux_instances_du_meme_appareil_qui_se_suivent_ne_portent_qu_un_en_tete()
    {
        Assert.Equal([true, false, false], DeviceHeaders.For(["A", "A", "A"]));
    }

    [Fact]
    public void Un_appareil_coupe_en_deux_recoit_un_en_tete_par_morceau()
    {
        Assert.Equal([true, true, true], DeviceHeaders.For(["A", "B", "A"]));
    }

    [Fact]
    public void Une_liste_vide_ne_donne_aucun_en_tete()
    {
        Assert.Empty(DeviceHeaders.For([]));
    }

    [Fact]
    public void Ce_qui_vaut_pour_l_appareil_ne_parait_qu_au_premier_morceau()
    {
        // The button that breaks the pairing has no reason to appear
        // twice for the same phone.
        Assert.Equal([true, true, false], DeviceHeaders.FirstOccurrences(["A", "B", "A"]));
    }
}

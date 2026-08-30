using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// Le nom d'un appareil n'apparaît que là où l'appareil change. Deux instances
/// du même téléphone qui se suivent n'en portent qu'un ; un téléphone coupé en
/// deux par une instance venue d'ailleurs en reçoit un par morceau.
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
        // Le bouton qui rompt l'association n'a aucune raison de paraître deux
        // fois pour le même téléphone.
        Assert.Equal([true, true, false], DeviceHeaders.FirstOccurrences(["A", "B", "A"]));
    }
}

using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// The phone's screen ratio, read from "wm size", held landscape: the ratio
/// the game is laid out for on that phone.
/// </summary>
public sealed class PhoneScreenTests
{
    [Fact]
    public void Un_telephone_en_portrait_est_lu_couche()
    {
        // Recorded on the 13T Pro.
        Assert.Equal(2712 / 1220.0, PhoneScreen.Aspect("Physical size: 1220x2712"));
    }

    [Fact]
    public void Une_tablette_en_paysage_est_lue_telle_quelle() =>
        Assert.Equal(2960 / 1848.0, PhoneScreen.Aspect("Physical size: 2960x1848"));

    [Fact]
    public void Une_taille_imposee_l_emporte_sur_la_taille_physique()
    {
        // What the system and the game use once "wm size" was changed.
        Assert.Equal(
            2960 / 1848.0,
            PhoneScreen.Aspect("Physical size: 1220x2712\nOverride size: 1848x2960"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cmd: Can't find service: window")]
    [InlineData("Physical size: 1x1000")]
    [InlineData("Physical size: 99999999999x2712")]
    public void Une_sortie_inconnue_ou_absurde_ne_donne_aucun_format(string output) =>
        Assert.Null(PhoneScreen.Aspect(output));
}

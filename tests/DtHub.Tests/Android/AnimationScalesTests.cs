using DtHub.Core.Android;

namespace DtHub.Tests.Android;

public class AnimationScalesTests
{
    [Theory]
    // Relevé sur le téléphone de référence : un appareil ordinaire répond ceci.
    [InlineData("1.0", 1.0)]
    [InlineData("0.0", 0.0)]
    [InlineData("0.5", 0.5)]
    [InlineData(" 2.0 \n", 2.0)]
    public void Une_valeur_lisible_est_lue(string output, double expected)
    {
        Assert.Equal(expected, AnimationScales.ParseScale(output));
    }

    [Theory]
    // « null » est ce qu'Android rend pour une valeur jamais fixée, et elle
    // vaut alors 1. Les autres cas sont des sorties d'échec.
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Exception occurred while executing")]
    [InlineData("-1")]
    public void Tout_ce_qui_ne_se_lit_pas_vaut_un(string? output)
    {
        // Se tromper vers 1 rend au téléphone son comportement d'origine. Se
        // tromper vers 0 lui laisserait les animations coupées sans que
        // personne ne l'ait demandé : l'erreur n'a pas le même prix des deux
        // côtés.
        Assert.Equal(1.0, AnimationScales.ParseScale(output));
    }

    [Fact]
    public void Les_trois_cles_sont_celles_d_Android()
    {
        Assert.Equal(
            ["window_animation_scale", "transition_animation_scale", "animator_duration_scale"],
            AnimationScales.Keys);
    }

    [Fact]
    public void Les_valeurs_suivent_l_ordre_des_cles()
    {
        var scales = new AnimationScales(1, 2, 3);

        Assert.Equal([1.0, 2.0, 3.0], scales.Values);
        Assert.Equal(AnimationScales.Keys.Length, scales.Values.Count);
    }

    [Fact]
    public void Un_telephone_deja_coupe_se_reconnait()
    {
        // On n'y touchera pas, et il n'y aura donc rien à lui rendre.
        Assert.True(AnimationScales.Off.AllOff);
        Assert.False(AnimationScales.Normal.AllOff);
        Assert.False(new AnimationScales(0, 0, 1).AllOff);
    }

    [Theory]
    [InlineData(0.0, "0.0")]
    [InlineData(1.0, "1.0")]
    [InlineData(0.5, "0.5")]
    public void L_ecriture_reste_lisible_par_Android(double scale, string expected)
    {
        // Point décimal, jamais la virgule : la commande part vers un shell
        // Android, qui ne connaît pas la culture de la machine.
        Assert.Equal(expected, AnimationScales.Text(scale));
    }

    [Fact]
    public void Ce_qui_est_ecrit_se_relit_a_l_identique()
    {
        foreach (var scale in new[] { 0.0, 0.5, 1.0, 2.0 })
        {
            Assert.Equal(scale, AnimationScales.ParseScale(AnimationScales.Text(scale)));
        }
    }
}

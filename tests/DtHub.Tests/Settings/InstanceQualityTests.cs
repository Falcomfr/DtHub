using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class InstanceQualityTests
{
    [Fact]
    public void Un_compte_sans_palier_suit_le_commun()
    {
        // Le défaut, et il doit rester le défaut : personne n'a à régler cinq
        // comptes pour que l'application marche.
        Assert.Equal(StreamQuality.Medium, InstanceQuality.Chosen(null, StreamQuality.Medium));

        Assert.Equal(
            QualityProfile.For(StreamQuality.Medium),
            InstanceQuality.ProfileFor(null, StreamQuality.Medium, null));
    }

    [Fact]
    public void Le_palier_du_compte_l_emporte_sur_le_commun()
    {
        Assert.Equal(StreamQuality.Low, InstanceQuality.Chosen(StreamQuality.Low, StreamQuality.Maximum));

        var profile = InstanceQuality.ProfileFor(StreamQuality.Low, StreamQuality.Maximum, null);

        Assert.Equal(QualityProfile.For(StreamQuality.Low), profile);
    }

    [Fact]
    public void Une_mule_en_palier_bas_coute_moins_que_le_principal_au_maximum()
    {
        // C'est tout l'intérêt de la fonction, dit en chiffres.
        var principal = InstanceQuality.ProfileFor(StreamQuality.Maximum, StreamQuality.Medium, null);
        var mule = InstanceQuality.ProfileFor(StreamQuality.Low, StreamQuality.Medium, null);

        Assert.True(mule.BitrateFor(1920, 1080) < principal.BitrateFor(1920, 1080));
        Assert.True(mule.MaxFps < principal.MaxFps);
        Assert.True(mule.MaximumDisplayHeight < principal.MaximumDisplayHeight);
    }

    [Fact]
    public void Le_palier_personnalise_borne_les_valeurs_absurdes()
    {
        // « QualityProfile.For » au palier personnalisé n'était couvert par
        // aucune épreuve : c'est le seul chemin qui accepte des nombres venus
        // de l'utilisateur.
        var fou = new CustomQuality { MaxFps = 100_000, MaximumDisplayHeight = 99_999, BitsPerPixel = 42 };

        var profile = InstanceQuality.ProfileFor(StreamQuality.Custom, StreamQuality.Custom, fou);

        Assert.InRange(profile.MaxFps, 1, 240);
        Assert.InRange(profile.MaximumDisplayHeight, 240, 7680);
        Assert.InRange(profile.BitsPerPixel, 0.03, 0.30);
    }

    [Fact]
    public void Le_palier_personnalise_sans_aucune_valeur_ne_leve_pas()
    {
        var profile = InstanceQuality.ProfileFor(StreamQuality.Custom, StreamQuality.Custom, null);

        Assert.True(profile.MaxFps > 0);
        Assert.True(profile.BitrateFor(1920, 1080) > 0);
    }

    [Theory]
    [InlineData(StreamQuality.Low)]
    [InlineData(StreamQuality.Medium)]
    [InlineData(StreamQuality.Maximum)]
    public void Les_cadences_restent_celles_du_palier_choisi(StreamQuality quality)
    {
        // Les cadences de sondage voyagent avec le palier, mais elles sont
        // propres à l'application : c'est l'appelant qui ne doit pas les
        // prendre ici, pas la fonction qui doit les retirer.
        var profile = InstanceQuality.ProfileFor(quality, StreamQuality.Medium, null);

        Assert.Equal(QualityProfile.For(quality).DevicePoll, profile.DevicePoll);
    }

    [Fact]
    public void Les_valeurs_fines_restent_communes()
    {
        // Il n'y a pas de réglage fin par compte, et c'est délibéré : ce
        // serait un champ persisté que rien n'exposerait.
        var commun = new CustomQuality { MaxFps = 37 };

        Assert.Equal(37, InstanceQuality.ProfileFor(null, StreamQuality.Custom, commun).MaxFps);
        Assert.Equal(37, InstanceQuality.ProfileFor(StreamQuality.Custom, StreamQuality.Medium, commun).MaxFps);
    }
}

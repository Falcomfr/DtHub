using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;

namespace DtHub.Tests.Scrcpy;

/// <summary>
/// L'afficheur suit la taille de la fenêtre, par paliers. C'est ce qui évite
/// une interface de jeu minuscule dans une petite fenêtre et floue en grand.
/// </summary>
public sealed class DisplayLadderTests
{
    [Fact]
    public void Une_petite_fenetre_recoit_une_petite_definition()
    {
        // Sans cela, l'image serait réduite d'un facteur quatre et l'interface
        // du jeu deviendrait illisible.
        var (width, height) = DisplayLadder.For(550, 3840, 2160);

        Assert.Equal(720, height);
        Assert.Equal(1280, width);
    }

    [Fact]
    public void Une_fenetre_plein_ecran_recoit_la_definition_de_l_ecran()
    {
        var (width, height) = DisplayLadder.For(2130, 3840, 2160);

        Assert.Equal(2160, height);
        Assert.Equal(3840, width);
    }

    [Fact]
    public void La_definition_ne_depasse_jamais_celle_de_l_ecran()
    {
        // Au-delà, l'encodeur du téléphone travaillerait pour des pixels que
        // personne ne verrait.
        var (_, height) = DisplayLadder.For(4000, 3840, 2160);

        Assert.Equal(2160, height);
    }

    [Fact]
    public void Le_palier_retenu_est_toujours_au_dessus_de_la_fenetre()
    {
        // L'image est alors réduite, jamais agrandie, donc toujours nette.
        foreach (var wanted in new[] { 400, 700, 901, 1080, 1500, 1900 })
        {
            var (_, height) = DisplayLadder.For(wanted, 3840, 2160);

            Assert.True(height >= wanted, $"palier {height} pour une fenêtre de {wanted}");
        }
    }

    [Fact]
    public void Le_rapport_est_celui_de_l_ecran()
    {
        // C'est lui que la fenêtre garde : s'en écarter laisserait une bande.
        var (width, height) = DisplayLadder.For(900, 2560, 1080);

        Assert.Equal(1080d / 2560, height / (double)width, 2);
    }

    [Fact]
    public void Les_cotes_sont_toujours_pairs()
    {
        var (width, height) = DisplayLadder.For(700, 1366, 768);

        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);
    }

    [Fact]
    public void Chaque_qualite_borne_la_definition_differemment()
    {
        // Sans borne, moyenne et haute auraient été indiscernables : le débit
        // ne se voit pas sur une image presque fixe.
        Assert.Equal(720, QualityProfile.For(StreamQuality.Low).MaximumDisplayHeight);
        Assert.Equal(1080, QualityProfile.For(StreamQuality.Medium).MaximumDisplayHeight);
        Assert.Equal(int.MaxValue, QualityProfile.For(StreamQuality.Maximum).MaximumDisplayHeight);

        // Trois paliers automatiques, pas quatre : deux voisins indiscernables
        // ne servaient qu'à faire hésiter. « Personnalisé » ne se compte pas
        // ici, ce n'est pas un barreau de plus sur l'échelle mais une sortie
        // de route, dont les valeurs viennent de l'utilisateur.
        Assert.Equal(
            3,
            Enum.GetValues<StreamQuality>().Count(q => q != StreamQuality.Custom));
    }

    [Fact]
    public void Le_palier_personnalise_borne_a_la_hauteur_choisie()
    {
        var profile = QualityProfile.For(
            StreamQuality.Custom,
            new CustomQuality { MaximumDisplayHeight = 1440, MaxFps = 45, BitrateKbps = 20000 });

        Assert.Equal(1440, profile.MaximumDisplayHeight);
        Assert.Equal(45, profile.MaxFps);

        // Le débit choisi est rendu tel quel, sans passer par le calcul en
        // bits par pixel ni par le plafond du palier moyen : celui qui saisit
        // un nombre a déjà tranché.
        Assert.Equal(20000, profile.BitrateFor(2560, 1440));
        Assert.Equal(20000, profile.BitrateFor(640, 360));
    }

    [Fact]
    public void Un_palier_personnalise_sans_valeurs_reste_ouvrable()
    {
        // Un fichier de réglages qui annonce le palier sans en porter les
        // valeurs doit rendre une session qui s'ouvre, pas une exception.
        var profile = QualityProfile.For(StreamQuality.Custom);

        Assert.Equal(CustomQuality.Default.MaxFps, profile.MaxFps);
        Assert.True(profile.BitrateFor(1920, 1080) > 0);
    }

    [Fact]
    public void La_definition_de_repli_est_celle_qu_aucun_encodeur_ne_refuse()
    {
        // Les encodeurs vidéo plafonnent, et pas tous au même endroit. Le
        // repli doit passer partout, y compris sur une tablette modeste.
        Assert.Equal(1920, DisplayLadder.FallbackWidth);
        Assert.Equal(1080, DisplayLadder.FallbackHeight);
    }

    [Fact]
    public void La_qualite_basse_borne_la_definition()
    {
        // C'est le principal levier : l'encodeur du téléphone travaille alors
        // sur quatre fois moins de pixels.
        var (_, height) = DisplayLadder.For(2130, 3840, 2160, maximumHeight: 720);

        Assert.Equal(720, height);
    }

    [Fact]
    public void Sans_ecran_connu_une_definition_de_repli_est_rendue()
    {
        var (width, height) = DisplayLadder.For(900, 0, 0);

        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public void Le_repli_descend_palier_par_palier()
    {
        // Un repli unique à 1080 laissait sans recours les encodeurs plafonnés
        // à 1280x720, courants sur le bas de gamme ancien et les tablettes
        // d'entrée de gamme.
        Assert.Equal(1080, DisplayLadder.Below(1440, 1920, 1080)!.Value.Height);
        Assert.Equal(720, DisplayLadder.Below(1080, 1920, 1080)!.Value.Height);
        Assert.Null(DisplayLadder.Below(720, 1280, 720));
        Assert.Null(DisplayLadder.Below(540, 960, 540));
    }

    [Fact]
    public void Le_repli_garde_le_rapport_d_image_de_l_ecran()
    {
        // Le repli imposait du 16:9, si bien que la fenêtre changeait de forme
        // entre la première tentative et la seconde sur un écran large.
        var (width, height) = DisplayLadder.Below(1440, 3440, 1440)!.Value;

        Assert.Equal(1080, height);
        Assert.Equal(2580, width);
    }

    [Fact]
    public void Un_rapport_inconnu_retombe_sur_la_definition_de_repli()
    {
        Assert.Equal((1920, 1080), DisplayLadder.At(1080, 0, 0));
    }
}

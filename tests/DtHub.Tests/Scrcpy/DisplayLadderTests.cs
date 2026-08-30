using DtHub.Core.Scrcpy;

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
}

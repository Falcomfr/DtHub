using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

/// <summary>
/// La première image : le seul instant où la fenêtre cesse d'être
/// noire, et que rien ne mesurait. Les lignes viennent du journal
/// d'une vraie session.
/// </summary>
public sealed class ScrcpyFirstImageTests
{
    [Theory]
    [InlineData("INFO: Texture: 1920x1080")]
    [InlineData("INFO: Texture: 1600x896")]
    [InlineData("Texture: 720x1600")]
    public void La_ligne_de_texture_annonce_la_premiere_image(string ligne)
    {
        Assert.True(ScrcpyOutputParser.IsFirstImage(ligne));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INFO: Renderer: direct3d11")]
    [InlineData("[server] INFO: New display: 1920x1080/240 (id=3)")]
    [InlineData("[server] INFO: Device: [Xiaomi] Xiaomi Mi 9T Pro (Android 11)")]
    [InlineData("ERROR: Server connection failed")]
    public void Le_reste_de_la_sortie_n_annonce_rien(string? ligne)
    {
        Assert.False(ScrcpyOutputParser.IsFirstImage(ligne));
    }

    [Fact]
    public void Le_rendu_precede_la_texture_et_ne_la_remplace_pas()
    {
        // scrcpy annonce son moteur de rendu dès l'ouverture, bien
        // avant d'avoir une image à y mettre : le confondre avec la
        // première image ramènerait la mesure à zéro.
        Assert.False(ScrcpyOutputParser.IsFirstImage("INFO: Renderer: direct3d11"));
        Assert.True(ScrcpyOutputParser.IsFirstImage("INFO: Texture: 1600x896"));
    }

    [Fact]
    public void Une_taille_sans_texture_ne_compte_pas()
    {
        Assert.False(ScrcpyOutputParser.IsFirstImage("INFO: 1600x896"));
    }
}

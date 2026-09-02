using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

/// <summary>
/// Le débit demandé à l'encodeur, palier par palier.
///
/// La mesure qui compte est le bit par pixel et par image : c'est ce qu'un
/// encodeur reçoit vraiment, et c'est elle qui était à l'envers.
/// </summary>
public sealed class QualityProfileTests
{
    private static double Bpp(StreamQuality quality, int width, int height)
    {
        var profile = QualityProfile.For(quality);

        return profile.BitrateFor(width, height) * 1000.0 / (width * (double)height * profile.MaxFps);
    }

    /// <summary>
    /// Chaque palier travaille à sa définition, celle que sa borne de hauteur
    /// autorise. Les trois doivent alors recevoir de quoi rendre une image
    /// propre, et le plus généreux le plus.
    /// </summary>
    [Fact]
    public void L_echelle_des_paliers_va_dans_le_bon_sens()
    {
        var basse = Bpp(StreamQuality.Low, 1280, 720);
        var moyenne = Bpp(StreamQuality.Medium, 1920, 1080);
        var maximale = Bpp(StreamQuality.Maximum, 2560, 1440);

        // Aucun palier affamé : la référence pour du H.264 de bonne facture
        // tourne autour de 0,10 bpp, et sous 0,045 l'image se délite.
        Assert.True(basse >= 0.045, $"basse à {basse:F3} bpp");
        Assert.True(moyenne >= 0.045, $"moyenne à {moyenne:F3} bpp");
        Assert.True(maximale >= 0.045, $"maximale à {maximale:F3} bpp");

        // Et l'échelle monte, ce qui n'était pas le cas : le palier maximal
        // recevait cinq fois et demie moins de bits par pixel que le plus bas.
        Assert.True(maximale >= moyenne, $"maximale {maximale:F3} contre moyenne {moyenne:F3}");
        Assert.True(moyenne >= basse, $"moyenne {moyenne:F3} contre basse {basse:F3}");
    }

    /// <summary>
    /// Le débit suit la définition à l'intérieur d'un même palier. Un débit
    /// fixe servait grassement une petite fenêtre et affamait une grande : à
    /// définition doublée en surface, il faut deux fois plus de bits.
    /// </summary>
    [Fact]
    public void Le_debit_suit_la_definition()
    {
        var profile = QualityProfile.For(StreamQuality.Maximum);

        var petite = profile.BitrateFor(960, 540);
        var grande = profile.BitrateFor(1920, 1080);

        Assert.True(grande > petite, $"{grande} contre {petite}");

        // Quatre fois la surface, quatre fois le débit, au plancher près.
        Assert.Equal(4.0, grande / (double)petite, 1.0);
    }

    [Theory]
    [InlineData(StreamQuality.Low, 30, 720)]
    [InlineData(StreamQuality.Medium, 60, 1080)]
    [InlineData(StreamQuality.Maximum, 60, int.MaxValue)]
    public void Chaque_palier_annonce_sa_cadence_et_sa_borne(
        StreamQuality quality,
        int fps,
        int hauteur)
    {
        var profile = QualityProfile.For(quality);

        Assert.Equal(fps, profile.MaxFps);
        Assert.Equal(hauteur, profile.MaximumDisplayHeight);
    }

    /// <summary>
    /// Le plafond existe pour une raison qui ne se lit pas dans le code : deux
    /// comptes ouverts, ce sont deux flux sur la même liaison sans fil.
    /// </summary>
    [Fact]
    public void Le_plafond_borne_les_tres_grandes_definitions()
    {
        var profile = QualityProfile.For(StreamQuality.Maximum);

        Assert.Equal(profile.CeilingKbps, profile.BitrateFor(7680, 4320));
        Assert.True(profile.CeilingKbps * 2 <= 60_000, "deux flux doivent tenir sur une liaison ordinaire");
    }

    /// <summary>
    /// Et le plancher aussi : sous ce seuil l'image serait une bouillie, et
    /// l'économie ne se sentirait sur rien.
    /// </summary>
    [Fact]
    public void Le_plancher_protege_les_toutes_petites_fenetres()
    {
        var profile = QualityProfile.For(StreamQuality.Low);

        Assert.Equal(QualityProfile.FloorKbps, profile.BitrateFor(320, 240));
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, -1)]
    public void Une_definition_absurde_ne_leve_pas(int width, int height) =>
        Assert.Equal(
            QualityProfile.FloorKbps,
            QualityProfile.For(StreamQuality.Medium).BitrateFor(width, height));

    /// <summary>
    /// Le palier basse allège par la définition et la cadence, non par une
    /// image dégradée : c'est le poste qui doit souffler, pas l'œil.
    /// </summary>
    [Fact]
    public void Le_palier_leger_allege_les_pixels_et_non_leur_finesse()
    {
        var basse = QualityProfile.For(StreamQuality.Low);
        var maximale = QualityProfile.For(StreamQuality.Maximum);

        // Un quart des pixels et la moitié de la cadence...
        Assert.True(basse.MaximumDisplayHeight * 2 <= 1440);
        Assert.True(basse.MaxFps * 2 <= maximale.MaxFps);

        // ... mais des bits par pixel du même ordre.
        Assert.True(basse.BitsPerPixel >= maximale.BitsPerPixel * 0.6);
    }
}

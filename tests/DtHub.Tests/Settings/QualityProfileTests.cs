using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

/// <summary>
/// The bitrate requested from the encoder, step by step.
///
/// The measure that matters is bits per pixel per frame: that is what an
/// encoder actually receives, and it is the one that was backwards.
/// </summary>
public sealed class QualityProfileTests
{
    private static double Bpp(StreamQuality quality, int width, int height)
    {
        var profile = QualityProfile.For(quality);

        return profile.BitrateFor(width, height) * 1000.0 / (width * (double)height * profile.MaxFps);
    }

    /// <summary>
    /// Each step works at its own resolution, the one its height cap
    /// allows. The three of them must therefore each receive enough to
    /// render a clean image, and the most generous step the most of all.
    /// </summary>
    [Fact]
    public void L_echelle_des_paliers_va_dans_le_bon_sens()
    {
        var basse = Bpp(StreamQuality.Low, 1280, 720);
        var moyenne = Bpp(StreamQuality.Medium, 1920, 1080);
        var maximale = Bpp(StreamQuality.Maximum, 2560, 1440);

        // No step starved: the reference for well-encoded H.264 sits
        // around 0.10 bpp, and below 0.045 the image falls apart.
        Assert.True(basse >= 0.045, $"basse à {basse:F3} bpp");
        Assert.True(moyenne >= 0.045, $"moyenne à {moyenne:F3} bpp");
        Assert.True(maximale >= 0.045, $"maximale à {maximale:F3} bpp");

        // And the scale climbs, which used not to be the case: the
        // maximum step used to receive five and a half times fewer bits
        // per pixel than the lowest one.
        Assert.True(maximale >= moyenne, $"maximale {maximale:F3} contre moyenne {moyenne:F3}");
        Assert.True(moyenne >= basse, $"moyenne {moyenne:F3} contre basse {basse:F3}");
    }

    /// <summary>
    /// The bitrate follows the resolution within a single step. A fixed
    /// bitrate used to serve a small window generously and starve a
    /// large one: with resolution doubled in surface, twice as many bits
    /// are needed.
    /// </summary>
    [Fact]
    public void Le_debit_suit_la_definition()
    {
        var profile = QualityProfile.For(StreamQuality.Maximum);

        var petite = profile.BitrateFor(960, 540);
        var grande = profile.BitrateFor(1920, 1080);

        Assert.True(grande > petite, $"{grande} contre {petite}");

        // Four times the surface, four times the bitrate, give or take
        // the floor.
        Assert.Equal(4.0, grande / (double)petite, 1.0);
    }

    [Theory]
    [InlineData(StreamQuality.Low, 30, 720)]
    [InlineData(StreamQuality.Medium, 60, 1080)]
    [InlineData(StreamQuality.Maximum, 60, 1440)]
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
    /// The ceiling exists for a reason that cannot be read in the code:
    /// two open accounts mean two streams on the same wireless link.
    /// </summary>
    [Fact]
    public void Le_plafond_borne_les_tres_grandes_definitions()
    {
        var profile = QualityProfile.For(StreamQuality.Maximum);

        Assert.Equal(profile.CeilingKbps, profile.BitrateFor(7680, 4320));
        Assert.True(profile.CeilingKbps * 2 <= 60_000, "deux flux doivent tenir sur une liaison ordinaire");
    }

    /// <summary>
    /// And the floor too: below this threshold the image would turn to
    /// mush, and the savings would show up nowhere.
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
    /// The low step lightens the load through resolution and frame rate,
    /// not through a degraded image: it is the machine that must catch
    /// its breath, not the eye.
    /// </summary>
    [Fact]
    public void Le_palier_leger_allege_les_pixels_et_non_leur_finesse()
    {
        var basse = QualityProfile.For(StreamQuality.Low);
        var maximale = QualityProfile.For(StreamQuality.Maximum);

        // A quarter of the pixels and half the frame rate...
        Assert.True(basse.MaximumDisplayHeight * 2 <= 1440);
        Assert.True(basse.MaxFps * 2 <= maximale.MaxFps);

        // ... but bits per pixel of the same order of magnitude.
        Assert.True(basse.BitsPerPixel >= maximale.BitsPerPixel * 0.6);
    }
}

using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;

namespace DtHub.Tests.Scrcpy;

/// <summary>
/// The display follows the window size, in steps. That is what keeps the
/// game interface from being tiny in a small window and blurry when
/// large.
/// </summary>
public sealed class DisplayLadderTests
{
    [Fact]
    public void Une_petite_fenetre_recoit_une_petite_definition()
    {
        // Without this, the image would be scaled down by a factor of four
        // and the game interface would become unreadable.
        var (width, height) = DisplayLadder.For(550, 3840, 2160);

        Assert.Equal(720, height);
        Assert.Equal(1280, width);
    }

    [Fact]
    public void Une_petite_fenetre_est_rendue_au_moins_en_1080()
    {
        // Reported on 2026-10-17: a 960x570 window, rendered at 952x536,
        // looked blurry at its own size. Rendered at 1080 and shrunk by the
        // PC, as "Sharper image" does, it was sharp.
        var (width, height) = DisplayLadder.For(550, 3840, 2160, minimumHeight: DisplayLadder.MinimumHeight);

        Assert.Equal(1080, height);
        Assert.Equal(1920, width);
    }

    [Fact]
    public void Le_plancher_ne_depasse_ni_la_qualite_ni_l_ecran()
    {
        // A mule set to the light tier keeps its ceiling.
        var (_, light) = DisplayLadder.For(550, 3840, 2160, maximumHeight: 720, minimumHeight: DisplayLadder.MinimumHeight);
        var (_, small) = DisplayLadder.For(550, 1366, 768, minimumHeight: DisplayLadder.MinimumHeight);

        Assert.Equal(720, light);
        Assert.Equal(768, small);
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
        // Beyond that, the phone's encoder would be working for pixels
        // that nobody would ever see.
        var (_, height) = DisplayLadder.For(4000, 3840, 2160);

        Assert.Equal(2160, height);
    }

    [Fact]
    public void Le_palier_retenu_est_toujours_au_dessus_de_la_fenetre()
    {
        // The image is then scaled down, never up, so it always stays
        // sharp.
        foreach (var wanted in new[] { 400, 700, 901, 1080, 1500, 1900 })
        {
            var (_, height) = DisplayLadder.For(wanted, 3840, 2160);

            Assert.True(height >= wanted, $"palier {height} pour une fenêtre de {wanted}");
        }
    }

    [Fact]
    public void Le_rapport_est_celui_de_l_ecran()
    {
        // This is the ratio the window keeps: straying from it would
        // leave a blank band.
        var (width, height) = DisplayLadder.For(900, 2560, 1080);

        Assert.Equal(1080d / 2560, height / (double)width, 2);
    }

    [Theory]
    [InlineData(3440, 1440, 2960, 1848)]
    [InlineData(3840, 2160, 2712, 1220)]
    [InlineData(1920, 1080, 2960, 1848)]
    public void Le_jeu_prend_le_format_de_l_ecran_du_telephone(
        int screenWidth, int screenHeight, int phoneLong, int phoneShort)
    {
        // The game draws itself right at its phone's own ratio, and not
        // always at another. Measured on 14/10/2026: a Galaxy Tab S10 Ultra
        // (16:10) shifted and cut in 21:9, right in 16:10; the 13T Pro
        // (20:9) cut its panel in 16:10, right in 20:9 and 21:9.
        var phone = phoneLong / (double)phoneShort;

        var (width, height) = DisplayLadder.For(900, screenWidth, screenHeight, phoneAspect: phone);

        Assert.Equal(phone, width / (double)height, precision: 2);
    }

    [Fact]
    public void Un_palier_garde_son_nombre_de_pixels_sur_un_telephone_allonge()
    {
        // A tier names a known load. Taken as a height, a 20:9 phone got
        // 2400x1080 in the medium tier, a quarter more pixels than the
        // 1920x1080 it stood for; the load is now the same at any ratio.
        var phone = 2712 / 1220.0;

        var (width, height) = DisplayLadder.For(1300, 3840, 2160, maximumHeight: 1080, phoneAspect: phone);

        Assert.True(width * height <= 1920 * 1080, $"{width}x{height}");
        Assert.True(width * height > 1920 * 1080 * 0.95, $"{width}x{height}");
        Assert.Equal(phone, width / (double)height, precision: 2);
    }

    [Fact]
    public void Un_telephone_moins_allonge_ne_depasse_pas_la_hauteur_du_palier() =>
        Assert.Equal((1728, 1080), DisplayLadder.For(1300, 3840, 2160, maximumHeight: 1080, phoneAspect: 1.6));

    [Fact]
    public void Un_telephone_plus_allonge_que_l_ecran_du_pc_est_borne_par_sa_largeur()
    {
        var (width, height) = DisplayLadder.For(1900, 1920, 1080, phoneAspect: 2712 / 1220.0);

        Assert.True(width <= 1920, $"{width}x{height} dépasse la largeur de l'écran");
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
        // Without a ceiling, medium and high would have been
        // indistinguishable: bitrate does not show on an almost still
        // image.
        Assert.Equal(720, QualityProfile.For(StreamQuality.Low).MaximumDisplayHeight);
        Assert.Equal(1080, QualityProfile.For(StreamQuality.Medium).MaximumDisplayHeight);
        Assert.Equal(1440, QualityProfile.For(StreamQuality.Maximum).MaximumDisplayHeight);

        // Three automatic steps, not four: two indistinguishable
        // neighbours only made people hesitate. Custom does not count
        // here, it is not one more rung on the ladder but an off-ramp,
        // whose values come from the user.
        Assert.Equal(
            3,
            Enum.GetValues<StreamQuality>().Count(q => q != StreamQuality.Custom));
    }

    [Fact]
    public void Le_palier_personnalise_borne_a_la_hauteur_choisie()
    {
        var profile = QualityProfile.For(
            StreamQuality.Custom,
            new CustomQuality { MaximumDisplayHeight = 1440, MaxFps = 45, BitsPerPixel = 0.12 });

        Assert.Equal(1440, profile.MaximumDisplayHeight);
        Assert.Equal(45, profile.MaxFps);
        Assert.Equal(0.12, profile.BitsPerPixel);
    }

    [Fact]
    public void Le_debit_personnalise_suit_la_definition()
    {
        // The lesson this file already held for the automatic steps, and
        // that a bitrate chosen in megabits would have undone: a fixed
        // bitrate serves a small window generously and starves a large
        // one. Detail (bits per pixel), though, keeps its meaning at
        // any size.
        var profile = QualityProfile.For(
            StreamQuality.Custom,
            new CustomQuality { MaximumDisplayHeight = 2160, MaxFps = 60, BitsPerPixel = 0.09 });

        var grande = profile.BitrateFor(2560, 1440);
        var petite = profile.BitrateFor(1280, 720);

        Assert.True(grande > petite, $"grande {grande} devrait dépasser petite {petite}");

        // And the detail actually served does match the one requested,
        // give or take rounding.
        Assert.Equal(0.09, petite * 1000.0 / (1280.0 * 720 * 60), 3);
    }

    [Fact]
    public void Le_palier_personnalise_garde_un_plafond()
    {
        // The ceiling does not protect against the user but against the
        // link: several open accounts mean several streams on the same
        // Wi-Fi.
        var profile = QualityProfile.For(
            StreamQuality.Custom,
            new CustomQuality { MaximumDisplayHeight = 2160, MaxFps = 60, BitsPerPixel = 0.30 });

        Assert.Equal(
            QualityProfile.For(StreamQuality.Maximum).CeilingKbps,
            profile.BitrateFor(3840, 2160));
    }

    [Fact]
    public void Un_palier_personnalise_sans_valeurs_reste_ouvrable()
    {
        // A settings file that declares the custom step without
        // carrying its values must still yield a session that opens,
        // not an exception.
        var profile = QualityProfile.For(StreamQuality.Custom);

        Assert.Equal(CustomQuality.Default.MaxFps, profile.MaxFps);
        Assert.True(profile.BitrateFor(1920, 1080) > 0);
    }

    [Fact]
    public void La_definition_de_repli_est_celle_qu_aucun_encodeur_ne_refuse()
    {
        // Video encoders cap out, and not all at the same point. The
        // fallback must work everywhere, including on a modest tablet.
        Assert.Equal(1920, DisplayLadder.FallbackWidth);
        Assert.Equal(1080, DisplayLadder.FallbackHeight);
    }

    [Fact]
    public void La_qualite_basse_borne_la_definition()
    {
        // This is the main lever: the phone's encoder then works on
        // four times fewer pixels.
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
        // A single fallback at 1080 left no recourse for encoders capped
        // at 1280x720, common on older low-end phones and entry-level
        // tablets.
        Assert.Equal(1080, DisplayLadder.Below(1440, 1920, 1080)!.Value.Height);
        Assert.Equal(720, DisplayLadder.Below(1080, 1920, 1080)!.Value.Height);
        Assert.Null(DisplayLadder.Below(720, 1280, 720));
        Assert.Null(DisplayLadder.Below(540, 960, 540));
    }

    [Theory]
    [InlineData(1440, 3440, 1440)]
    [InlineData(1080, 2400, 1080)]
    public void Le_repli_garde_le_rapport_et_tient_dans_1920_de_large(int height, int width, int ratioHeight)
    {
        // The fallback used to force 16:9, so the window changed shape
        // between the first attempt and the second on a wide display. It
        // keeps the ratio, and now the width too: it exists for encoders
        // capped at 1920x1088, which a 20:9 phone's 2400x1080 exceeds.
        var (fallbackWidth, fallbackHeight) = DisplayLadder.Below(height, width, ratioHeight)!.Value;

        Assert.True(fallbackWidth <= 1920, $"{fallbackWidth}x{fallbackHeight}");
        Assert.Equal(width / (double)ratioHeight, fallbackWidth / (double)fallbackHeight, precision: 1);
        Assert.True(fallbackWidth > 1600, $"Le repli est descendu trop bas : {fallbackWidth}x{fallbackHeight}");

        // Both sides are sizes the encoder will render without cropping.
        Assert.Equal(0, fallbackWidth % 8);
        Assert.Equal(0, fallbackHeight % 8);
    }

    [Fact]
    public void Un_rapport_inconnu_retombe_sur_la_definition_de_repli()
    {
        Assert.Equal((1920, 1080), DisplayLadder.At(1080, 0, 0));
    }

    [Fact]
    public void L_image_plus_nette_double_la_hauteur_demandee()
    {
        // The video carries colour at half resolution. Measured on the 13T
        // Pro on 2026-10-12: red text on grey, window 1915x800, smeared at
        // 2144x896 and as sharp as white text at 3440x1440.
        // 2136 since the width follows the aligned height.
        Assert.Equal((2136, 896), DisplayLadder.For(835, 3440, 1440, 1440));
        Assert.Equal((3440, 1440), DisplayLadder.For(835, 3440, 1440, 1440, sharpImage: true));
    }

    [Fact]
    public void L_image_plus_nette_reste_sous_le_plafond_du_palier()
    {
        // A tier still names a known load for the phone.
        var (_, height) = DisplayLadder.For(835, 3440, 1440, 1080, sharpImage: true);

        Assert.Equal(1080, height);
    }
}

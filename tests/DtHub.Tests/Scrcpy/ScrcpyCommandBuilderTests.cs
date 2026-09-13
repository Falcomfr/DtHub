using DtHub.Core;
using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

public class ScrcpyCommandBuilderTests
{
    private static string Line(IReadOnlyList<string> arguments) => string.Join(' ', arguments);

    /// <summary>
    /// Value of an option, written glued to it with an equals sign.
    /// </summary>
    private static string? ValueOf(IReadOnlyList<string> arguments, string option)
    {
        var prefix = option + "=";

        return arguments.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))
            ?[prefix.Length..];
    }

    [Fact]
    public void Les_reglages_par_defaut_correspondent_a_ce_qui_est_annonce()
    {
        var options = ScrcpyOptions.Default;

        Assert.Equal(45, options.MaxFps);
        Assert.Equal(4000, options.VideoBitrateKbps);
        Assert.False(options.AudioEnabled);
        Assert.True(options.ClipboardSyncEnabled);
    }

    [Fact]
    public void L_appareil_le_titre_et_les_reglages_de_base_sont_transmis()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default);

        Assert.Equal("USB0001", ValueOf(arguments, "--serial"));
        Assert.Equal("Titre", ValueOf(arguments, "--window-title"));
        Assert.Equal("45", ValueOf(arguments, "--max-fps"));
        Assert.Equal("4000K", ValueOf(arguments, "--video-bit-rate"));
    }

    [Fact]
    public void Le_clavier_est_en_mode_sdk()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default);

        Assert.Equal("sdk", ValueOf(arguments, "--keyboard"));
    }

    [Fact]
    public void La_saisie_de_texte_privilegiee_reste_disponible_mais_eteinte()
    {
        // It swallows the modifier keys: it is a setting, not a default.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default with { PreferText = true });

        Assert.Contains("--prefer-text", arguments);
    }

    [Fact]
    public void Le_mode_clavier_materiel_est_transmis_quand_il_est_choisi()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default with { KeyboardMode = ScrcpyKeyboardMode.Uhid });

        Assert.Equal("uhid", ValueOf(arguments, "--keyboard"));
    }

    [Fact]
    public void Le_mode_souris_materielle_est_transmis_quand_il_est_choisi()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default with { MouseMode = ScrcpyMouseMode.Uhid });

        Assert.Equal("uhid", ValueOf(arguments, "--mouse"));
    }

    [Fact]
    public void La_souris_passe_par_l_injection_par_defaut()
    {
        // The remedy captures the machine's own cursor: it must never
        // be chosen on its own.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default);

        Assert.Equal("sdk", ValueOf(arguments, "--mouse"));
    }

    [Fact]
    public void L_audio_desactive_ajoute_l_option_correspondante()
    {
        var muted = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", ScrcpyOptions.Default);
        var withAudio = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T", ScrcpyOptions.Default with { AudioEnabled = true });

        Assert.Contains("--no-audio", muted);
        Assert.DoesNotContain("--no-audio", withAudio);
    }

    [Fact]
    public void Le_presse_papiers_reste_synchronise_par_defaut()
    {
        var synced = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", ScrcpyOptions.Default);
        var unsynced = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T", ScrcpyOptions.Default with { ClipboardSyncEnabled = false });

        Assert.DoesNotContain("--no-clipboard-autosync", synced);
        Assert.Contains("--no-clipboard-autosync", unsynced);
    }

    [Fact]
    public void L_afficheur_virtuel_est_demande_avec_sa_definition_et_sa_densite()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", ScrcpyOptions.Default);

        Assert.Equal("1920x1080/240", ValueOf(arguments, "--new-display"));
        Assert.Contains("--no-vd-system-decorations", arguments);
    }

    [Fact]
    public void Sans_afficheur_virtuel_les_options_correspondantes_disparaissent()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T", ScrcpyOptions.Default with { UseVirtualDisplay = false });

        Assert.DoesNotContain("--new-display", arguments);
        Assert.DoesNotContain("--no-vd-system-decorations", arguments);
    }

    [Fact]
    public void Les_cotes_impairs_sont_ramenes_a_des_nombres_pairs()
    {
        // Video encoders refuse odd-numbered sides.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T",
            ScrcpyOptions.Default with { VirtualDisplayWidth = 2599, VirtualDisplayHeight = 1461 });

        Assert.Equal("2598x1460/240", ValueOf(arguments, "--new-display"));
    }

    [Fact]
    public void La_taille_de_fenetre_est_transmise_directement()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T",
            ScrcpyOptions.Default,
            new ScrcpyWindowPlacement(100, 50, 1280, 720));

        Assert.Equal("1280", ValueOf(arguments, "--window-width"));
        Assert.Equal("720", ValueOf(arguments, "--window-height"));
        Assert.Equal("1920x1080/240", ValueOf(arguments, "--new-display"));
    }

    [Fact]
    public void Sans_position_connue_aucune_option_de_fenetre_n_est_ajoutee()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", ScrcpyOptions.Default);

        Assert.DoesNotContain("--window-x", arguments);
    }

    [Fact]
    public void Chaque_valeur_est_accolee_a_son_option()
    {
        // Three scrcpy options accept an optional value, including
        // --new-display. For those, a value separated by a space is
        // taken for a stray argument, and scrcpy refuses to start.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre",
            ScrcpyOptions.Default,
            new ScrcpyWindowPlacement(1, 2, 3, 4));

        Assert.Contains("--new-display=1920x1080/240", arguments);

        foreach (var argument in arguments.Where(a => a.StartsWith("--", StringComparison.Ordinal)))
        {
            // An option carries its value, or has none; never a
            // detached value in the list.
            Assert.DoesNotContain(' ', argument.Split('=')[0]);
        }

        // No token must be an orphaned value.
        Assert.All(arguments, a => Assert.StartsWith("--", a, StringComparison.Ordinal));
    }

    [Fact]
    public void Le_serveur_adb_partage_n_est_jamais_tue_avec_une_session()
    {
        // Safeguard: --kill-adb-on-close would cut ADB for the whole
        // machine.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", ScrcpyOptions.Default);

        Assert.DoesNotContain("--kill-adb-on-close", arguments);
    }

    [Fact]
    public void Des_reglages_aberrants_sont_corriges_plutot_que_refuses()
    {
        var options = ScrcpyOptions.Default with
        {
            MaxFps = 0,
            VideoBitrateKbps = -50,
            VirtualDisplayWidth = 1,
            VirtualDisplayDpi = 100000,
        };

        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", options);

        Assert.Equal("1", ValueOf(arguments, "--max-fps"));
        Assert.Equal("200K", ValueOf(arguments, "--video-bit-rate"));
        Assert.Equal("240x1080/800", ValueOf(arguments, "--new-display"));
    }

    [Fact]
    public void Un_appareil_non_renseigne_est_refuse()
    {
        Assert.Throws<ArgumentException>(() =>
            ScrcpyCommandBuilder.BuildMirrorArguments("  ", "T", ScrcpyOptions.Default));
    }

    [Fact]
    public void Le_titre_de_fenetre_prefixe_le_nom_choisi_par_celui_du_produit()
    {
        // The product name only appears there: in the taskbar, to
        // recognize the game's windows among the others.
        Assert.Equal($"{ProductInfo.Name} XSpace", ScrcpyCommandBuilder.BuildWindowTitle("XSpace"));
        Assert.Equal($"{ProductInfo.Name} Enutrof", ScrcpyCommandBuilder.BuildWindowTitle("  Enutrof  "));
    }

    [Fact]
    public void Un_nom_vide_retombe_sur_le_nom_du_produit()
    {
        Assert.Equal(ProductInfo.Name, ScrcpyCommandBuilder.BuildWindowTitle(null));
        Assert.Equal(ProductInfo.Name, ScrcpyCommandBuilder.BuildWindowTitle("   "));
    }

    [Fact]
    public void La_liste_des_applications_vise_bien_l_appareil_demande()
    {
        var arguments = ScrcpyCommandBuilder.BuildListAppsArguments("USB0001");

        Assert.Equal(["--serial=USB0001", "--list-apps"], arguments);
        Assert.DoesNotContain("--new-display", Line(arguments), StringComparison.Ordinal);
    }

    [Fact]
    public void Le_titre_rappelle_le_raccourci_de_changement_de_compte()
    {
        // The windows overlap and look alike: the reminder can be read
        // above the image, without opening anything.
        Assert.Equal(
            $"{ProductInfo.Name} XSpace  (Ctrl + Tab : fenêtre suivante)",
            ScrcpyCommandBuilder.BuildWindowTitle("XSpace", "Ctrl + Tab : fenêtre suivante"));
    }

    [Fact]
    public void Sans_raccourci_le_titre_reste_le_nom_seul()
    {
        Assert.Equal($"{ProductInfo.Name} XSpace", ScrcpyCommandBuilder.BuildWindowTitle("XSpace", "   "));
    }

    [Fact]
    public void Les_clics_secondaires_ne_declenchent_rien_par_defaut()
    {
        // Otherwise scrcpy binds the right click to BACK, which quits
        // the game and leaves a black screen. Shift restores all four
        // actions.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "DT Hub", ScrcpyOptions.Default, null);

        Assert.Equal("----:bhsn", ValueOf(arguments, "--mouse-bind"));
    }

    [Fact]
    public void Le_collage_tape_le_texte_et_les_modificateurs_sont_respectes()
    {
        // Measured on the phone: with --prefer-text, Ctrl+V used to type
        // a "v" into the field instead of pasting, since alphabetic
        // keys go out as text events. And without --legacy-paste,
        // ordinary pasting inserted nothing: scrcpy did place the text
        // in Android's clipboard, its log says so, but the PASTE key
        // drew nothing from it.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default);

        Assert.DoesNotContain("--prefer-text", arguments);
        Assert.Contains("--legacy-paste", arguments);
    }

    [Theory]
    [InlineData("h265", "h265")]
    [InlineData("  AV1  ", "av1")]
    public void Un_codec_connu_de_scrcpy_est_transmis(string configured, string expected)
    {
        var options = ScrcpyOptions.Default with { VideoCodec = configured };

        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", options);

        Assert.Equal(expected, ValueOf(arguments, "--video-codec"));
    }

    [Fact]
    public void Un_codec_inconnu_est_ecarte_plutot_que_transmis()
    {
        // A name scrcpy does not recognize makes it exit right away, in
        // a form nothing knows how to translate: the user would get the
        // generic message after the full timeout, over a typo.
        var options = ScrcpyOptions.Default with { VideoCodec = "h266" };

        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", options);

        Assert.DoesNotContain(arguments, a => a.StartsWith("--video-codec", StringComparison.Ordinal));
    }

    [Fact]
    public void L_encodeur_et_la_cadence_ne_paraissent_que_si_on_les_demande()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default);

        Assert.DoesNotContain(arguments, a => a.StartsWith("--video-encoder", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, a => string.Equals(a, "--print-fps", StringComparison.Ordinal));
    }

    [Fact]
    public void L_encodeur_impose_arrive_dans_la_commande()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001",
            "Titre",
            ScrcpyOptions.Default with { VideoEncoder = "c2.mtk.avc.encoder" });

        Assert.Contains("--video-encoder=c2.mtk.avc.encoder", arguments);
    }

    [Fact]
    public void Le_diagnostic_de_fluidite_arrive_dans_la_commande()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default with { PrintFps = true });

        Assert.Contains("--print-fps", arguments);
    }

    [Fact]
    public void La_liste_des_encodeurs_n_ouvre_ni_fenetre_ni_afficheur()
    {
        var arguments = ScrcpyCommandBuilder.BuildListEncodersArguments("USB0001");

        Assert.Equal(["--serial=USB0001", "--list-encoders"], arguments);
    }
}

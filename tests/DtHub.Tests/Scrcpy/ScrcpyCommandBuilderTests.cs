using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

public class ScrcpyCommandBuilderTests
{
    private static string Line(IReadOnlyList<string> arguments) => string.Join(' ', arguments);

    private static string? ValueAfter(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
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

        Assert.Equal("USB0001", ValueAfter(arguments, "--serial"));
        Assert.Equal("Titre", ValueAfter(arguments, "--window-title"));
        Assert.Equal("45", ValueAfter(arguments, "--max-fps"));
        Assert.Equal("4000K", ValueAfter(arguments, "--video-bit-rate"));
    }

    [Fact]
    public void Le_clavier_est_en_mode_sdk_avec_saisie_de_texte_privilegiee()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default);

        Assert.Equal("sdk", ValueAfter(arguments, "--keyboard"));
        Assert.Contains("--prefer-text", arguments);
    }

    [Fact]
    public void Le_mode_clavier_materiel_est_transmis_quand_il_est_choisi()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default with { KeyboardMode = ScrcpyKeyboardMode.Uhid });

        Assert.Equal("uhid", ValueAfter(arguments, "--keyboard"));
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

        Assert.Equal("1080x1920/320", ValueAfter(arguments, "--new-display"));
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
    public void La_position_initiale_de_la_fenetre_est_transmise_quand_elle_est_connue()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T", ScrcpyOptions.Default, new ScrcpyWindowPlacement(100, 50, 1280, 720));

        Assert.Equal("100", ValueAfter(arguments, "--window-x"));
        Assert.Equal("50", ValueAfter(arguments, "--window-y"));
        Assert.Equal("1280", ValueAfter(arguments, "--window-width"));
        Assert.Equal("720", ValueAfter(arguments, "--window-height"));
    }

    [Fact]
    public void Sans_position_connue_aucune_option_de_fenetre_n_est_ajoutee()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("USB0001", "T", ScrcpyOptions.Default);

        Assert.DoesNotContain("--window-x", arguments);
    }

    [Fact]
    public void Le_serveur_adb_partage_n_est_jamais_tue_avec_une_session()
    {
        // Garde-fou : --kill-adb-on-close couperait ADB pour toute la machine.
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

        Assert.Equal("1", ValueAfter(arguments, "--max-fps"));
        Assert.Equal("200K", ValueAfter(arguments, "--video-bit-rate"));
        Assert.Equal("240x1920/640", ValueAfter(arguments, "--new-display"));
    }

    [Fact]
    public void Un_appareil_non_renseigne_est_refuse()
    {
        Assert.Throws<ArgumentException>(() =>
            ScrcpyCommandBuilder.BuildMirrorArguments("  ", "T", ScrcpyOptions.Default));
    }

    [Fact]
    public void Le_titre_de_fenetre_porte_l_identifiant_de_session()
    {
        var withName = ScrcpyCommandBuilder.BuildWindowTitle("abc123", "DOFUS Touch - Clone");
        var withoutName = ScrcpyCommandBuilder.BuildWindowTitle("abc123");

        Assert.Equal("DOFUS Touch - Clone - DtHub [abc123]", withName);
        Assert.Equal("DtHub [abc123]", withoutName);
        Assert.Contains("abc123", withName, StringComparison.Ordinal);
    }

    [Fact]
    public void La_liste_des_applications_vise_bien_l_appareil_demande()
    {
        var arguments = ScrcpyCommandBuilder.BuildListAppsArguments("USB0001");

        Assert.Equal(["--serial", "USB0001", "--list-apps"], arguments);
        Assert.DoesNotContain("--new-display", Line(arguments), StringComparison.Ordinal);
    }
}

using DtHub.Core;
using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

public class ScrcpyCommandBuilderTests
{
    private static string Line(IReadOnlyList<string> arguments) => string.Join(' ', arguments);

    /// <summary>Valeur d'une option, écrite accolée par un signe égal.</summary>
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
    public void Le_clavier_est_en_mode_sdk_avec_saisie_de_texte_privilegiee()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre", ScrcpyOptions.Default);

        Assert.Equal("sdk", ValueOf(arguments, "--keyboard"));
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
    public void Avec_l_ajustement_continu_l_afficheur_nait_a_la_taille_de_la_fenetre()
    {
        // scrcpy refuse --window-width et --window-height dans ce mode : la
        // taille se règle par la définition de l'afficheur. Les deux côtés
        // viennent donc de la fenêtre, et la hauteur surtout : le jeu fige la
        // hauteur de sa mise en page à son initialisation.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T",
            ScrcpyOptions.Default with { FlexDisplay = true, VirtualDisplayHeight = 2160 },
            new ScrcpyWindowPlacement(100, 50, 1280, 720));

        Assert.Equal("100", ValueOf(arguments, "--window-x"));
        Assert.Equal("50", ValueOf(arguments, "--window-y"));
        Assert.Equal("1280x720/240", ValueOf(arguments, "--new-display"));
        Assert.DoesNotContain(arguments, a => a.StartsWith("--window-width", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, a => a.StartsWith("--window-height", StringComparison.Ordinal));
        Assert.Contains("--flex-display", arguments);
    }

    [Fact]
    public void Les_cotes_impairs_sont_ramenes_a_des_nombres_pairs()
    {
        // Les encodeurs vidéo refusent les côtés impairs.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T",
            ScrcpyOptions.Default with { FlexDisplay = true },
            new ScrcpyWindowPlacement(0, 0, 2599, 1461));

        Assert.Equal("2598x1460/240", ValueOf(arguments, "--new-display"));
    }

    [Fact]
    public void La_hauteur_de_naissance_suit_la_fenetre_et_non_les_reglages()
    {
        // Le jeu fige la hauteur de sa mise en page quand il s'initialise :
        // elle doit donc être la bonne dès la naissance de l'afficheur. Les
        // réglages, eux, ne valent que pour la définition fixe.
        var basse = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T",
            ScrcpyOptions.Default with { FlexDisplay = true, VirtualDisplayHeight = 2160 },
            new ScrcpyWindowPlacement(0, 0, 1600, 900));

        var haute = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T",
            ScrcpyOptions.Default with { FlexDisplay = true, VirtualDisplayHeight = 2160 },
            new ScrcpyWindowPlacement(0, 0, 1600, 1400));

        Assert.Equal("1600x900/240", ValueOf(basse, "--new-display"));
        Assert.Equal("1600x1400/240", ValueOf(haute, "--new-display"));
    }

    [Fact]
    public void Sans_ajustement_continu_la_taille_de_fenetre_est_transmise_directement()
    {
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "T",
            ScrcpyOptions.Default with { FlexDisplay = false },
            new ScrcpyWindowPlacement(100, 50, 1280, 720));

        Assert.Equal("1280", ValueOf(arguments, "--window-width"));
        Assert.Equal("720", ValueOf(arguments, "--window-height"));
        Assert.Equal("1920x1080/240", ValueOf(arguments, "--new-display"));
        Assert.DoesNotContain("--flex-display", arguments);
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
        // Trois options de scrcpy acceptent une valeur facultative, dont
        // --new-display. Pour celles-là, une valeur séparée par une espace est
        // prise pour un argument parasite et scrcpy refuse de démarrer.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "Titre",
            ScrcpyOptions.Default with { FlexDisplay = true },
            new ScrcpyWindowPlacement(1, 2, 3, 4));

        // Les côtés impairs sont ramenés à des nombres pairs.
        Assert.Contains("--new-display=2x4/240", arguments);

        foreach (var argument in arguments.Where(a => a.StartsWith("--", StringComparison.Ordinal)))
        {
            // Une option porte sa valeur, ou n'en a pas ; jamais de valeur
            // détachée dans la liste.
            Assert.DoesNotContain(' ', argument.Split('=')[0]);
        }

        // Aucun jeton ne doit être une valeur orpheline.
        Assert.All(arguments, a => Assert.StartsWith("--", a, StringComparison.Ordinal));
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

        Assert.Equal("1", ValueOf(arguments, "--max-fps"));
        Assert.Equal("200K", ValueOf(arguments, "--video-bit-rate"));
        Assert.Equal("240x1080/640", ValueOf(arguments, "--new-display"));
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
        // Le nom du produit n'apparaît que là : dans la barre des tâches, pour
        // reconnaître les fenêtres du jeu parmi les autres.
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
        // Les fenêtres se superposent et se ressemblent : le rappel se lit
        // au-dessus de l'image, sans rien ouvrir.
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
        // scrcpy associe sinon le clic droit à RETOUR, ce qui quitte le jeu et
        // laisse un écran noir. Maj rétablit les quatre actions.
        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments(
            "USB0001", "DT Hub", ScrcpyOptions.Default, null);

        Assert.Equal("----:bhsn", ValueOf(arguments, "--mouse-bind"));
    }
}

using DtHub.Core.Hotkeys;

namespace DtHub.Tests.Hotkeys;

public class HotkeySetTests
{
    [Fact]
    public void Les_raccourcis_par_defaut_sont_ceux_annonces()
    {
        var set = HotkeySet.Default;

        Assert.Equal("Ctrl + P", set.For(HotkeyAction.ToggleConfigurator)!.DisplayText);
        Assert.Equal("Ctrl + Tab", set.For(HotkeyAction.NextInstance)!.DisplayText);
        Assert.Equal("Ctrl + Maj + Tab", set.For(HotkeyAction.PreviousInstance)!.DisplayText);
        Assert.Equal("Ctrl + R", set.For(HotkeyAction.Rearrange)!.DisplayText);
        Assert.Equal("Ctrl + 0", set.For(HotkeyAction.Quit)!.DisplayText);
    }

    [Fact]
    public void Toutes_les_actions_ont_un_raccourci_par_defaut()
    {
        var set = HotkeySet.Default;

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            Assert.NotNull(set.For(action));
            Assert.True(set.For(action)!.IsAssigned, $"{action} n'a pas de raccourci.");
        }
    }

    [Fact]
    public void Aucun_raccourci_par_defaut_n_est_en_double()
    {
        var combinations = HotkeySet.Default.Bindings.Select(b => b.Combination).ToList();

        Assert.Equal(combinations.Count, combinations.Distinct().Count());
    }

    [Fact]
    public void Une_combinaison_est_resolue_vers_son_action()
    {
        var set = HotkeySet.Default;

        Assert.Equal(HotkeyAction.NextInstance, set.Resolve(VirtualKeys.Tab, HotkeyModifiers.Control));
        Assert.Equal(
            HotkeyAction.PreviousInstance,
            set.Resolve(VirtualKeys.Tab, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.Null(set.Resolve(VirtualKeys.Tab, HotkeyModifiers.Alt));
    }

    [Fact]
    public void Un_doublon_est_detecte_et_l_action_fautive_est_nommee()
    {
        var set = HotkeySet.Default;

        var result = set.Validate(HotkeyAction.Rearrange, VirtualKeys.P, HotkeyModifiers.Control);

        Assert.Equal(HotkeyValidationResult.Duplicate, result);
        Assert.Equal(
            HotkeyAction.ToggleConfigurator,
            set.FindConflict(HotkeyAction.Rearrange, VirtualKeys.P, HotkeyModifiers.Control));
    }

    [Fact]
    public void Reattribuer_le_meme_raccourci_a_la_meme_action_n_est_pas_un_conflit()
    {
        var set = HotkeySet.Default;

        Assert.Equal(
            HotkeyValidationResult.Valid,
            set.Validate(HotkeyAction.ToggleConfigurator, VirtualKeys.P, HotkeyModifiers.Control));
    }

    [Fact]
    public void Une_touche_de_modification_seule_est_refusee()
    {
        var set = HotkeySet.Default;

        Assert.Equal(
            HotkeyValidationResult.ModifierOnly,
            set.Validate(HotkeyAction.Rearrange, VirtualKeys.Control, HotkeyModifiers.Control));

        Assert.Equal(
            HotkeyValidationResult.ModifierOnly,
            set.Validate(HotkeyAction.Rearrange, VirtualKeys.Shift, HotkeyModifiers.Shift));
    }

    [Fact]
    public void Une_touche_sans_modificateur_est_refusee()
    {
        // Sinon le raccourci se déclencherait à chaque frappe dans le jeu.
        Assert.Equal(
            HotkeyValidationResult.MissingModifier,
            HotkeySet.Default.Validate(HotkeyAction.Rearrange, VirtualKeys.A, HotkeyModifiers.None));
    }

    [Fact]
    public void Une_touche_de_fonction_est_acceptee_sans_modificateur()
    {
        Assert.Equal(
            HotkeyValidationResult.Valid,
            HotkeySet.Default.Validate(HotkeyAction.Rearrange, VirtualKeys.F12, HotkeyModifiers.None));
    }

    [Fact]
    public void Une_saisie_vide_est_signalee()
    {
        Assert.Equal(
            HotkeyValidationResult.NoKey,
            HotkeySet.Default.Validate(HotkeyAction.Rearrange, 0, HotkeyModifiers.Control));
    }

    [Theory]
    [InlineData(VirtualKeys.Delete, HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    [InlineData(0x4C, HotkeyModifiers.Windows)]
    [InlineData(VirtualKeys.Tab, HotkeyModifiers.Alt)]
    [InlineData(VirtualKeys.Escape, HotkeyModifiers.Alt)]
    public void Les_combinaisons_reservees_par_windows_sont_refusees(int key, HotkeyModifiers modifiers)
    {
        Assert.Equal(
            HotkeyValidationResult.ReservedBySystem,
            HotkeySet.Default.Validate(HotkeyAction.Rearrange, key, modifiers));
    }

    [Fact]
    public void Un_raccourci_valide_remplace_l_ancien()
    {
        var set = HotkeySet.Default.With(
            HotkeyAction.Rearrange, VirtualKeys.F5, HotkeyModifiers.Control | HotkeyModifiers.Shift);

        Assert.Equal("Ctrl + Maj + F5", set.For(HotkeyAction.Rearrange)!.DisplayText);
        Assert.Null(set.Resolve(VirtualKeys.R, HotkeyModifiers.Control));
    }

    [Fact]
    public void Un_raccourci_refuse_laisse_l_ensemble_intact()
    {
        var original = HotkeySet.Default;

        var unchanged = original.With(HotkeyAction.Rearrange, VirtualKeys.P, HotkeyModifiers.Control);

        Assert.Equal("Ctrl + R", unchanged.For(HotkeyAction.Rearrange)!.DisplayText);
        Assert.Equal(HotkeyAction.ToggleConfigurator, unchanged.Resolve(VirtualKeys.P, HotkeyModifiers.Control));
    }

    [Fact]
    public void Une_action_peut_etre_privee_de_raccourci()
    {
        var set = HotkeySet.Default.Without(HotkeyAction.Quit);

        Assert.False(set.For(HotkeyAction.Quit)!.IsAssigned);
        Assert.Equal("Non attribué", set.For(HotkeyAction.Quit)!.DisplayText);
        Assert.Null(set.Resolve(VirtualKeys.D0, HotkeyModifiers.Control));
    }

    [Fact]
    public void Restaurer_les_valeurs_par_defaut_reprend_tout_l_ensemble()
    {
        var modified = HotkeySet.Default
            .Without(HotkeyAction.Rearrange)
            .With(HotkeyAction.NextInstance, VirtualKeys.F9, HotkeyModifiers.Control);

        Assert.Equal("Ctrl + F9", modified.For(HotkeyAction.NextInstance)!.DisplayText);
        Assert.Equal("Ctrl + Tab", HotkeySet.Default.For(HotkeyAction.NextInstance)!.DisplayText);
    }

    [Fact]
    public void Un_fichier_contenant_des_doublons_est_reparé_a_la_lecture()
    {
        // Deux actions revendiquent Ctrl+1 : la seconde est écartée et
        // l'action concernée reprend sa valeur par défaut.
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.ToggleConfigurator, VirtualKey = VirtualKeys.P, Modifiers = HotkeyModifiers.Control },
            new HotkeyBinding { Action = HotkeyAction.Rearrange, VirtualKey = VirtualKeys.P, Modifiers = HotkeyModifiers.Control },
        };

        var set = HotkeySet.FromBindings(bindings);

        Assert.Equal(HotkeyAction.ToggleConfigurator, set.Resolve(VirtualKeys.P, HotkeyModifiers.Control));
        Assert.Equal("Ctrl + R", set.For(HotkeyAction.Rearrange)!.DisplayText);
    }

    [Fact]
    public void Un_fichier_contenant_un_raccourci_invalide_le_remplace_par_le_defaut()
    {
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.Rearrange, VirtualKey = VirtualKeys.A, Modifiers = HotkeyModifiers.None },
        };

        Assert.Equal("Ctrl + R", HotkeySet.FromBindings(bindings).For(HotkeyAction.Rearrange)!.DisplayText);
    }

    [Fact]
    public void Un_fichier_vide_donne_les_raccourcis_par_defaut()
    {
        var set = HotkeySet.FromBindings(null);

        Assert.Equal("Ctrl + Tab", set.For(HotkeyAction.NextInstance)!.DisplayText);
        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, set.Bindings.Count);
    }

    [Fact]
    public void Une_action_volontairement_sans_raccourci_le_reste_apres_relecture()
    {
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.Quit, VirtualKey = 0 },
        };

        Assert.False(HotkeySet.FromBindings(bindings).For(HotkeyAction.Quit)!.IsAssigned);
    }

    [Fact]
    public void Un_raccourci_par_defaut_deja_pris_par_l_utilisateur_n_est_pas_reattribue()
    {
        // L'utilisateur a mis Ctrl+R sur « Session suivante ». « Recentrer » ne
        // doit pas le reprendre au chargement.
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.NextInstance, VirtualKey = VirtualKeys.R, Modifiers = HotkeyModifiers.Control },
        };

        var set = HotkeySet.FromBindings(bindings);

        Assert.Equal(HotkeyAction.NextInstance, set.Resolve(VirtualKeys.R, HotkeyModifiers.Control));
        Assert.False(set.For(HotkeyAction.Rearrange)!.IsAssigned);
    }

    [Fact]
    public void Les_libelles_d_action_sont_lisibles()
    {
        Assert.Equal("Fenêtre suivante", HotkeyBinding.DescribeAction(HotkeyAction.NextInstance));
        Assert.Equal("Remettre les fenêtres en place", HotkeyBinding.DescribeAction(HotkeyAction.Rearrange));
        Assert.Equal("Quitter", HotkeyBinding.DescribeAction(HotkeyAction.Quit));
    }

    [Theory]
    [InlineData(VirtualKeys.Tab, "Tab")]
    [InlineData(VirtualKeys.A, "A")]
    [InlineData(VirtualKeys.D0, "0")]
    [InlineData(VirtualKeys.F1, "F1")]
    [InlineData(0x7B, "F12")]
    [InlineData(VirtualKeys.Escape, "Échap")]
    [InlineData(VirtualKeys.NumPad0, "Pavé 0")]
    [InlineData(0, "(aucune)")]
    public void Chaque_touche_a_un_libelle(int key, string expected)
    {
        Assert.Equal(expected, VirtualKeys.Describe(key));
    }

    [Fact]
    public void Une_touche_inconnue_reste_affichable()
    {
        Assert.Contains("Touche", VirtualKeys.Describe(0xFE), StringComparison.Ordinal);
    }

    [Fact]
    public void Les_modificateurs_sont_affiches_dans_un_ordre_stable()
    {
        var binding = new HotkeyBinding
        {
            Action = HotkeyAction.Rearrange,
            VirtualKey = VirtualKeys.A,
            Modifiers = HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Alt,
        };

        Assert.Equal("Ctrl + Alt + Maj + A", binding.DisplayText);
    }
}

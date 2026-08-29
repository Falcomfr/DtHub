using DtHub.Core.Hotkeys;

namespace DtHub.Tests.Hotkeys;

public class HotkeySetTests
{
    [Fact]
    public void Les_raccourcis_par_defaut_sont_ceux_annonces()
    {
        var set = HotkeySet.Default;

        Assert.Equal("Ctrl + Tab", set.For(HotkeyAction.NextSession)!.DisplayText);
        Assert.Equal("Ctrl + 1", set.For(HotkeyAction.Size1)!.DisplayText);
        Assert.Equal("Ctrl + 2", set.For(HotkeyAction.Size2)!.DisplayText);
        Assert.Equal("Ctrl + 3", set.For(HotkeyAction.Size3)!.DisplayText);
        Assert.Equal("Ctrl + 4", set.For(HotkeyAction.Size4)!.DisplayText);
        Assert.Equal("Ctrl + 5", set.For(HotkeyAction.Fullscreen)!.DisplayText);
        Assert.Equal("Ctrl + R", set.For(HotkeyAction.Recenter)!.DisplayText);
        Assert.Equal("Ctrl + 0", set.For(HotkeyAction.CloseAllSessions)!.DisplayText);
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

        Assert.Equal(HotkeyAction.NextSession, set.Resolve(VirtualKeys.Tab, HotkeyModifiers.Control));
        Assert.Equal(
            HotkeyAction.PreviousSession,
            set.Resolve(VirtualKeys.Tab, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.Null(set.Resolve(VirtualKeys.Tab, HotkeyModifiers.Alt));
    }

    [Fact]
    public void Un_doublon_est_detecte_et_l_action_fautive_est_nommee()
    {
        var set = HotkeySet.Default;

        var result = set.Validate(HotkeyAction.Recenter, VirtualKeys.D1, HotkeyModifiers.Control);

        Assert.Equal(HotkeyValidationResult.Duplicate, result);
        Assert.Equal(
            HotkeyAction.Size1,
            set.FindConflict(HotkeyAction.Recenter, VirtualKeys.D1, HotkeyModifiers.Control));
    }

    [Fact]
    public void Reattribuer_le_meme_raccourci_a_la_meme_action_n_est_pas_un_conflit()
    {
        var set = HotkeySet.Default;

        Assert.Equal(
            HotkeyValidationResult.Valid,
            set.Validate(HotkeyAction.Size1, VirtualKeys.D1, HotkeyModifiers.Control));
    }

    [Fact]
    public void Une_touche_de_modification_seule_est_refusee()
    {
        var set = HotkeySet.Default;

        Assert.Equal(
            HotkeyValidationResult.ModifierOnly,
            set.Validate(HotkeyAction.Recenter, VirtualKeys.Control, HotkeyModifiers.Control));

        Assert.Equal(
            HotkeyValidationResult.ModifierOnly,
            set.Validate(HotkeyAction.Recenter, VirtualKeys.Shift, HotkeyModifiers.Shift));
    }

    [Fact]
    public void Une_touche_sans_modificateur_est_refusee()
    {
        // Sinon le raccourci se déclencherait à chaque frappe dans le jeu.
        Assert.Equal(
            HotkeyValidationResult.MissingModifier,
            HotkeySet.Default.Validate(HotkeyAction.Recenter, VirtualKeys.A, HotkeyModifiers.None));
    }

    [Fact]
    public void Une_touche_de_fonction_est_acceptee_sans_modificateur()
    {
        Assert.Equal(
            HotkeyValidationResult.Valid,
            HotkeySet.Default.Validate(HotkeyAction.Recenter, VirtualKeys.F12, HotkeyModifiers.None));
    }

    [Fact]
    public void Une_saisie_vide_est_signalee()
    {
        Assert.Equal(
            HotkeyValidationResult.NoKey,
            HotkeySet.Default.Validate(HotkeyAction.Recenter, 0, HotkeyModifiers.Control));
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
            HotkeySet.Default.Validate(HotkeyAction.Recenter, key, modifiers));
    }

    [Fact]
    public void Un_raccourci_valide_remplace_l_ancien()
    {
        var set = HotkeySet.Default.With(
            HotkeyAction.Recenter, VirtualKeys.F5, HotkeyModifiers.Control | HotkeyModifiers.Shift);

        Assert.Equal("Ctrl + Maj + F5", set.For(HotkeyAction.Recenter)!.DisplayText);
        Assert.Null(set.Resolve(VirtualKeys.R, HotkeyModifiers.Control));
    }

    [Fact]
    public void Un_raccourci_refuse_laisse_l_ensemble_intact()
    {
        var original = HotkeySet.Default;

        var unchanged = original.With(HotkeyAction.Recenter, VirtualKeys.D1, HotkeyModifiers.Control);

        Assert.Equal("Ctrl + R", unchanged.For(HotkeyAction.Recenter)!.DisplayText);
        Assert.Equal(HotkeyAction.Size1, unchanged.Resolve(VirtualKeys.D1, HotkeyModifiers.Control));
    }

    [Fact]
    public void Une_action_peut_etre_privee_de_raccourci()
    {
        var set = HotkeySet.Default.Without(HotkeyAction.CloseAllSessions);

        Assert.False(set.For(HotkeyAction.CloseAllSessions)!.IsAssigned);
        Assert.Equal("Non attribué", set.For(HotkeyAction.CloseAllSessions)!.DisplayText);
        Assert.Null(set.Resolve(VirtualKeys.D0, HotkeyModifiers.Control));
    }

    [Fact]
    public void Restaurer_les_valeurs_par_defaut_reprend_tout_l_ensemble()
    {
        var modified = HotkeySet.Default
            .Without(HotkeyAction.Recenter)
            .With(HotkeyAction.NextSession, VirtualKeys.F9, HotkeyModifiers.Control);

        Assert.Equal("Ctrl + F9", modified.For(HotkeyAction.NextSession)!.DisplayText);
        Assert.Equal("Ctrl + Tab", HotkeySet.Default.For(HotkeyAction.NextSession)!.DisplayText);
    }

    [Fact]
    public void Un_fichier_contenant_des_doublons_est_reparé_a_la_lecture()
    {
        // Deux actions revendiquent Ctrl+1 : la seconde est écartée et
        // l'action concernée reprend sa valeur par défaut.
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.Size1, VirtualKey = VirtualKeys.D1, Modifiers = HotkeyModifiers.Control },
            new HotkeyBinding { Action = HotkeyAction.Recenter, VirtualKey = VirtualKeys.D1, Modifiers = HotkeyModifiers.Control },
        };

        var set = HotkeySet.FromBindings(bindings);

        Assert.Equal(HotkeyAction.Size1, set.Resolve(VirtualKeys.D1, HotkeyModifiers.Control));
        Assert.Equal("Ctrl + R", set.For(HotkeyAction.Recenter)!.DisplayText);
    }

    [Fact]
    public void Un_fichier_contenant_un_raccourci_invalide_le_remplace_par_le_defaut()
    {
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.Recenter, VirtualKey = VirtualKeys.A, Modifiers = HotkeyModifiers.None },
        };

        Assert.Equal("Ctrl + R", HotkeySet.FromBindings(bindings).For(HotkeyAction.Recenter)!.DisplayText);
    }

    [Fact]
    public void Un_fichier_vide_donne_les_raccourcis_par_defaut()
    {
        var set = HotkeySet.FromBindings(null);

        Assert.Equal("Ctrl + Tab", set.For(HotkeyAction.NextSession)!.DisplayText);
        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, set.Bindings.Count);
    }

    [Fact]
    public void Une_action_volontairement_sans_raccourci_le_reste_apres_relecture()
    {
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.CloseAllSessions, VirtualKey = 0 },
        };

        Assert.False(HotkeySet.FromBindings(bindings).For(HotkeyAction.CloseAllSessions)!.IsAssigned);
    }

    [Fact]
    public void Un_raccourci_par_defaut_deja_pris_par_l_utilisateur_n_est_pas_reattribue()
    {
        // L'utilisateur a mis Ctrl+R sur « Session suivante ». « Recentrer » ne
        // doit pas le reprendre au chargement.
        var bindings = new[]
        {
            new HotkeyBinding { Action = HotkeyAction.NextSession, VirtualKey = VirtualKeys.R, Modifiers = HotkeyModifiers.Control },
        };

        var set = HotkeySet.FromBindings(bindings);

        Assert.Equal(HotkeyAction.NextSession, set.Resolve(VirtualKeys.R, HotkeyModifiers.Control));
        Assert.False(set.For(HotkeyAction.Recenter)!.IsAssigned);
    }

    [Fact]
    public void Les_libelles_d_action_sont_lisibles()
    {
        Assert.Equal("Session suivante", HotkeyBinding.DescribeAction(HotkeyAction.NextSession));
        Assert.Equal("Recentrer les fenêtres", HotkeyBinding.DescribeAction(HotkeyAction.Recenter));
        Assert.Equal("Fermer toutes les sessions", HotkeyBinding.DescribeAction(HotkeyAction.CloseAllSessions));
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
            Action = HotkeyAction.Recenter,
            VirtualKey = VirtualKeys.A,
            Modifiers = HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Alt,
        };

        Assert.Equal("Ctrl + Alt + Maj + A", binding.DisplayText);
    }
}

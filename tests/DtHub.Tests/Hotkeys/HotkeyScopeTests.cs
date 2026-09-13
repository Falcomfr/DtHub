using DtHub.Core.Hotkeys;

namespace DtHub.Tests.Hotkeys;

/// <summary>
/// The rule that keeps shortcuts from being global. It is a promise
/// displayed in the README, and it had no safety net: a regression
/// would have confiscated Ctrl+Tab and Ctrl+R from the browser
/// without anything turning red.
/// </summary>
public class HotkeyScopeTests
{
    private static readonly SessionWindow[] Deux =
    [
        new(Handle: 1000, ProcessId: 42),
        new(Handle: 2000, ProcessId: 43),
    ];

    [Fact]
    public void Une_fenetre_de_jeu_au_premier_plan_arme_les_raccourcis()
    {
        Assert.True(HotkeyScope.Holds(1000, owner: 42, Deux, ours: false));
    }

    [Fact]
    public void Une_fenetre_etrangere_les_desarme()
    {
        // The case that matters: the browser in the foreground must
        // get Ctrl+Tab back.
        Assert.False(HotkeyScope.Holds(9999, owner: 777, Deux, ours: false));
    }

    [Fact]
    public void Le_processus_suffit_quand_le_handle_n_est_pas_encore_resolu()
    {
        // A freshly reopened session does not yet have its handle:
        // without this path, the shortcuts stayed off until you
        // clicked elsewhere and then back on a game window.
        Assert.True(HotkeyScope.Holds(4321, owner: 43, Deux, ours: false));
    }

    [Fact]
    public void Un_processus_inconnu_ne_vaut_pas_reconnaissance()
    {
        // Zero means "I could not tell." Treating it as an ordinary
        // value would arm the shortcuts on any window whose process
        // cannot be read.
        Assert.False(HotkeyScope.Holds(4321, owner: 0, [new(Handle: 1000, ProcessId: 0)], ours: false));
    }

    [Fact]
    public void Une_fenetre_a_nous_arme_les_raccourcis_sans_aucune_session()
    {
        // The configurator, the guides, a linked page, or the tabbed
        // frame. This last one was missing: clicking the tab bar
        // used to turn off all 12 shortcuts, Ctrl+P included.
        Assert.True(HotkeyScope.Holds(5555, owner: 0, [], ours: true));
    }

    [Fact]
    public void Sans_rien_d_ouvert_ni_de_reconnu_ils_restent_en_veille()
    {
        Assert.False(HotkeyScope.Holds(5555, owner: 12, [], ours: false));
    }
}

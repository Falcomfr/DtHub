using DtHub.Core.Windows;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Windows;

public class ModifierKeyReleaseTests
{
    private readonly FakeWindowController _windows = new();

    [Fact]
    public void Apres_un_raccourci_chaque_fenetre_de_jeu_apprend_que_les_modificateurs_sont_relaches()
    {
        // The window left by a Ctrl+Tab got the Ctrl press and never its
        // release: it went to the next window. Measured on 2026-09-27, it
        // then turned every click into a pinch until Ctrl was pressed again
        // in it.
        new ModifierKeyRelease(_windows).Release([0x1200, 0x1300]);

        Assert.Equal([0x1200, 0x1300], _windows.ModifierReleases);
    }

    [Fact]
    public void Apres_un_raccourci_la_touche_encore_tenue_n_empeche_rien()
    {
        // Ctrl is still down when the shortcut fires: that is how the
        // shortcut was typed. The window left behind needs its release now.
        _windows.ModifierDown = true;

        new ModifierKeyRelease(_windows).Release([0x1200]);

        Assert.Equal([0x1200], _windows.ModifierReleases);
    }

    [Fact]
    public void Une_fenetre_absente_ou_citee_deux_fois_n_est_prevenue_qu_une_fois()
    {
        new ModifierKeyRelease(_windows).Release([0x1200, 0, 0x1200]);

        Assert.Equal([0x1200], _windows.ModifierReleases);
    }

    [Fact]
    public void Au_premier_plan_sans_touche_tenue_les_fenetres_sont_prevenues()
    {
        // Back on an account by a click, a tab or Alt+Tab: nothing holds
        // Ctrl, so a window that still believes it held is wrong.
        var sent = new ModifierKeyRelease(_windows).ReleaseUnlessHeld([0x1200, 0x1300]);

        Assert.True(sent);
        Assert.Equal([0x1200, 0x1300], _windows.ModifierReleases);
    }

    [Fact]
    public void Au_premier_plan_une_touche_encore_tenue_est_laissee_a_la_fenetre()
    {
        // Arrived by Ctrl+Tab with Ctrl still down: the window will get the
        // real release when the player lets go. Sending one now would lie
        // to it while the key is held.
        _windows.ModifierDown = true;

        var sent = new ModifierKeyRelease(_windows).ReleaseUnlessHeld([0x1200]);

        Assert.False(sent);
        Assert.Empty(_windows.ModifierReleases);
    }
}

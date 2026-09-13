using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// What the lock means for an account docked in the tabbed frame.
///
/// Since the two toggles are independent, an account could be
/// locked and docked, and the frame would still resize it: the lock
/// protected nothing there while promising the opposite.
/// </summary>
public class FrameLockTests
{
    [Fact]
    public void Un_compte_loge_et_verrouille_fige_le_cadre()
    {
        Assert.True(FrameLock.Freezes(["principal"], ["principal", "xspace"]));
    }

    [Fact]
    public void Un_seul_cadenas_suffit_meme_si_les_autres_sont_libres()
    {
        // This is the accepted cost of the rule: a lock is a
        // protection, and a neighbor does not lift another one's
        // protection.
        Assert.True(FrameLock.Freezes(["xspace"], ["principal", "xspace", "second"]));
    }

    [Fact]
    public void Un_compte_verrouille_reste_libre_hors_du_cadre()
    {
        Assert.False(FrameLock.Freezes(["principal"], ["xspace"]));
    }

    [Fact]
    public void Sans_cadenas_le_cadre_suit_les_placements()
    {
        Assert.False(FrameLock.Freezes([], ["principal", "xspace"]));
    }

    [Fact]
    public void Un_cadre_vide_n_est_jamais_fige()
    {
        Assert.False(FrameLock.Freezes(["principal"], []));
    }

    [Fact]
    public void La_comparaison_des_clefs_est_exacte()
    {
        // Keys come from the settings and are case-sensitive:
        // matching them carelessly would freeze a frame at random.
        Assert.False(FrameLock.Freezes(["Principal"], ["principal"]));
    }
}

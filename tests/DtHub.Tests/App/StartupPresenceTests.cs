using DtHub.Core.Sessions;

namespace DtHub.Tests.App;

public class StartupPresenceTests
{
    [Fact]
    public void Le_panneau_reste_masque_quand_des_fenetres_s_ouvrent()
    {
        Assert.False(StartupPresence.ShowConfigurator(remembered: false, openedWindows: 2));
    }

    [Fact]
    public void Le_panneau_reparait_s_il_etait_affiche()
    {
        Assert.True(StartupPresence.ShowConfigurator(remembered: true, openedWindows: 2));
    }

    [Fact]
    public void Sans_aucune_fenetre_le_panneau_parait_meme_s_il_etait_masque()
    {
        // This is the case where all instances have been closed
        // from the panel: nothing is checked anymore, nothing
        // opens, and hiding it would leave an application with
        // nothing on screen and no way to find it again, since the
        // window titles are the only place where the shortcut is
        // reminded.
        Assert.True(StartupPresence.ShowConfigurator(remembered: false, openedWindows: 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Montrer_avant_le_lancement_ne_mene_jamais_a_reprendre_le_panneau(int ouvertes)
    {
        // This is the whole safety of showing it ahead of time: if
        // the panel appears before knowing how many windows will
        // open, then no number of windows must later be able to
        // make it disappear, or the user would see a panel flicker
        // at startup.
        foreach (var retenu in new[] { true, false })
        {
            if (StartupPresence.ShowBeforeLaunch(retenu))
            {
                Assert.True(StartupPresence.ShowConfigurator(retenu, ouvertes));
            }
        }
    }

    [Fact]
    public void Un_panneau_masque_a_la_sortie_attend_le_resultat_du_lancement()
    {
        // Hidden on exit, it only comes back if nothing opens: that
        // is only known afterward, so we do not anticipate it.
        Assert.False(StartupPresence.ShowBeforeLaunch(remembered: false));
    }

    [Fact]
    public void Un_panneau_affiche_a_la_sortie_parait_sans_attendre()
    {
        Assert.True(StartupPresence.ShowBeforeLaunch(remembered: true));
    }
}

using DtHub.Core.IPhone;
using DtHub.Core.Sessions;

namespace DtHub.Tests.IPhone;

/// <summary>An iPhone's window lives as an Android account's window does.</summary>
public class IosWindowLifeTests
{
    [Fact]
    public void Un_debranchement_garde_le_compte_ouvert_et_met_le_temps_en_pause()
    {
        var life = new IosWindowLife();
        life.Opened();
        Assert.True(life.CountsPlaytime);

        life.Unplug();

        Assert.Equal(IosWindowState.Unplugged, life.State);
        Assert.False(life.CountsPlaytime);

        life.Replugged();
        Assert.Equal(IosWindowState.Opening, life.State);
        life.Opened();
        Assert.True(life.CountsPlaytime);
        Assert.Equal(0, life.Attempts);
    }

    [Fact]
    public void Une_erreur_rouvre_jusqu_au_nombre_d_essais_d_Android_puis_abandonne()
    {
        var life = new IosWindowLife();
        life.Opened();

        for (var attempt = 1; attempt <= SessionRecovery.MaxAttempts; attempt++)
        {
            Assert.Null(life.Fail(SessionRecovery.MaxAttempts));
            Assert.Equal(IosWindowState.Failed, life.State);
            Assert.Equal(attempt, life.Attempts);
            Assert.False(life.CountsPlaytime);
            life.Replugged();
            life.Opened();
        }

        Assert.Equal(IosEnd.GaveUp, life.Fail(SessionRecovery.MaxAttempts));
        Assert.Equal(IosWindowState.Closed, life.State);

        // Android's rule too: a quiet spell gives the credit back.
        var later = new IosWindowLife();
        later.Opened();
        Assert.Null(later.Fail(SessionRecovery.MaxAttempts));
        later.Forget();
        Assert.Equal(0, later.Attempts);
    }

    [Fact]
    public void Une_fermeture_a_la_croix_retire_le_compte_du_demarrage()
    {
        var life = new IosWindowLife();
        life.Opened();

        var end = life.Close(byHand: true);

        Assert.Equal(IosEnd.ByHand, end);
        Assert.Equal(IosWindowState.Closed, life.State);
        Assert.False(IosWindowLife.KeepsStartup(end));
    }

    [Fact]
    public void Un_abandon_garde_le_compte_au_demarrage()
    {
        var life = new IosWindowLife();
        life.Opened();

        var end = life.Fail(0);

        Assert.Equal(IosEnd.GaveUp, end);
        Assert.True(IosWindowLife.KeepsStartup(IosEnd.GaveUp));
    }

    [Fact]
    public void Une_fermeture_par_DT_Hub_garde_le_demarrage()
    {
        var life = new IosWindowLife();
        life.Unplug();

        var end = life.Close(byHand: false);

        Assert.Equal(IosEnd.ByUs, end);
        Assert.Equal(IosWindowState.Closed, life.State);
        Assert.True(IosWindowLife.KeepsStartup(end));
    }

    [Fact]
    public void Une_fenetre_fermee_ne_se_rouvre_pas_et_ne_finit_pas_deux_fois()
    {
        var life = new IosWindowLife();
        life.Opened();
        life.Close(byHand: false);

        life.Opened();
        Assert.Equal(IosWindowState.Closed, life.State);
        Assert.False(life.CountsPlaytime);

        Assert.Null(life.Fail(SessionRecovery.MaxAttempts));
        Assert.Equal(0, life.Attempts);
        Assert.Equal(IosWindowState.Closed, life.State);

        life.Unplug();
        life.Replugged();
        Assert.Equal(IosWindowState.Closed, life.State);

        life.Close(byHand: true);
        Assert.Equal(IosWindowState.Closed, life.State);
    }

    [Fact]
    public void Un_debranchement_apres_une_erreur_attend_l_appareil()
    {
        var life = new IosWindowLife();
        life.Opened();
        Assert.Null(life.Fail(SessionRecovery.MaxAttempts));

        life.Unplug();

        Assert.Equal(IosWindowState.Unplugged, life.State);
        life.Replugged();
        Assert.Equal(IosWindowState.Opening, life.State);
        Assert.Equal(1, life.Attempts);
    }

    [Fact]
    public void Une_fenetre_qui_attend_son_prochain_essai_garde_DT_Hub_ouvert()
    {
        var life = new IosWindowLife();
        Assert.True(life.KeepsAppAlive);

        life.Opened();
        Assert.True(life.KeepsAppAlive);

        life.Unplug();
        Assert.True(life.KeepsAppAlive);

        life.Replugged();
        life.Opened();
        Assert.Null(life.Fail(3));
        Assert.Equal(IosWindowState.Failed, life.State);
        Assert.True(life.KeepsAppAlive);

        // Given up, its window saying why: nothing more will come.
        var givenUp = new IosWindowLife();
        Assert.Equal(IosEnd.GaveUp, givenUp.Fail(0));
        Assert.False(givenUp.KeepsAppAlive);

        life.Close(byHand: true);
        Assert.False(life.KeepsAppAlive);
    }
}

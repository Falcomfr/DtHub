using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;

namespace DtHub.Tests.Sessions;

public class InstanceActivityTests
{
    [Fact]
    public void Une_session_qui_demarre_se_distingue_d_une_session_qui_tourne()
    {
        Assert.Equal(
            InstanceActivity.Starting,
            InstanceActivities.Of(ScrcpySessionState.Starting, recovering: false));

        Assert.Equal(
            InstanceActivity.Running,
            InstanceActivities.Of(ScrcpySessionState.Running, recovering: false));
    }

    [Fact]
    public void Sans_session_ni_reprise_le_compte_est_au_repos()
    {
        Assert.Equal(InstanceActivity.Idle, InstanceActivities.Of(null, recovering: false));
    }

    [Fact]
    public void Sans_session_mais_en_reprise_le_compte_se_reconnecte()
    {
        Assert.Equal(InstanceActivity.Recovering, InstanceActivities.Of(null, recovering: true));
    }

    /// <summary>
    /// A session that exists says more than the recovery flag, which
    /// outlives its own attempt by up to forty-five seconds: the window
    /// is back, and the row must not keep announcing a reconnection.
    /// </summary>
    [Fact]
    public void Une_session_vivante_l_emporte_sur_un_avis_de_reprise()
    {
        Assert.Equal(
            InstanceActivity.Running,
            InstanceActivities.Of(ScrcpySessionState.Running, recovering: true));
    }

    /// <summary>
    /// The contract three XAML bindings hang off. Getting it wrong
    /// swaps the play button for the stop button.
    /// </summary>
    [Fact]
    public void Seules_les_sessions_vivantes_comptent_pour_une_fenetre()
    {
        Assert.True(InstanceActivity.Starting.HasWindow());
        Assert.True(InstanceActivity.Running.HasWindow());
        Assert.False(InstanceActivity.Recovering.HasWindow());
        Assert.False(InstanceActivity.Idle.HasWindow());
    }
}

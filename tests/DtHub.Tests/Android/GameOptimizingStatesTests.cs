using DtHub.Core.Android;

namespace DtHub.Tests.Android;

/// <summary>What DT Hub remembers of each Samsung's game optimizing service (D237).</summary>
public class GameOptimizingStatesTests
{
    [Fact]
    public void Une_lecture_ratee_sera_retentee()
    {
        // A Wi-Fi Samsung whose first read timed out must not lose the button for the session.
        var states = new GameOptimizingStates();

        Assert.True(states.Begin("A"));
        states.Finish("A", null);

        Assert.True(states.Begin("A"));
    }

    [Fact]
    public void Une_lecture_reussie_n_est_pas_refaite()
    {
        var states = new GameOptimizingStates();

        Assert.True(states.Begin("A"));
        states.Finish("A", GameOptimizingState.On);

        Assert.False(states.Begin("A"));
        Assert.Equal(GameOptimizingState.On, states.Of("A"));
    }

    [Fact]
    public void Un_appareil_parti_n_est_pas_reecrit_par_sa_lecture()
    {
        var states = new GameOptimizingStates();

        Assert.True(states.Begin("A"));
        states.Forget("A");
        states.Finish("A", GameOptimizingState.On);

        Assert.Null(states.Of("A"));
        Assert.True(states.Begin("A"));
    }

    [Fact]
    public void Un_changement_remplace_l_etat_connu()
    {
        var states = new GameOptimizingStates();

        states.Set("A", GameOptimizingState.Off);

        Assert.Equal(GameOptimizingState.Off, states.Of("A"));
    }
}

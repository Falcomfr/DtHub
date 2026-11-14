using DtHub.Core.Hotkeys;

namespace DtHub.Tests.Hotkeys;

/// <summary>
/// The foreground read every 200 ms, for the change Windows does not announce:
/// a tabbed frame made active while DT Hub was not in front, then clicked, goes
/// to the foreground without its event (measured 2026-11-14), and Ctrl+Tab
/// stayed dead.
/// </summary>
public class ForegroundWatchTests
{
    [Fact]
    public void Un_changement_de_premier_plan_est_signale_une_seule_fois()
    {
        var watch = new ForegroundWatch();

        Assert.True(watch.Saw(100));
        Assert.False(watch.Saw(100));
        Assert.True(watch.Saw(200));
    }
}

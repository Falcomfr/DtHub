using DtHub.Core.IPhone;

namespace DtHub.Tests.IPhone;

public class IosPlaytimeTests
{
    [Fact]
    public void Le_temps_compte_tant_que_la_fenetre_est_ouverte_et_s_arrete_au_debranchement()
    {
        const string key = "U1|0|ios";
        var start = new DateTimeOffset(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);
        var playtime = new IosPlaytime();

        Assert.Empty(playtime.Update([key], start));
        Assert.Empty(playtime.Update([key], start.AddSeconds(30)));

        // Unplugged: the window stays, its time stops.
        Assert.Equal([(key, 100)], playtime.Update([], start.AddSeconds(100)));
        Assert.Empty(playtime.Update([], start.AddSeconds(150)));

        // Back, then closed: only the time after the return.
        Assert.Empty(playtime.Update([key], start.AddSeconds(200)));
        Assert.Equal([(key, 50)], playtime.Update([], start.AddSeconds(250)));
    }
}

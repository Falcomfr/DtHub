using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

public class GameWindowListTests
{
    private sealed record FakeGameWindow(string Key, bool IsAlive = true) : IGameWindow
    {
        public string Id => Key;

        public nint WindowHandle => 1;

        public double SourceAspectRatio => 0;

        public string WindowTitle => Key;

        public int ProcessId => 1;
    }

    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private static Func<string, int> Ranks(params string[] keys) =>
        key => Array.IndexOf(keys, key) is var rank and >= 0 ? rank : int.MaxValue;

    [Fact]
    public void Les_fenetres_iOS_et_Android_suivent_l_ordre_de_la_liste()
    {
        var a = new FakeGameWindow("A|0|fr.ankama");
        var b = new FakeGameWindow("B|0|ios");
        var c = new FakeGameWindow("C|0|fr.ankama");

        // As the launcher passes them: the phones' sessions, then the iPhones.
        IGameWindow[] windows = [a, c, b];
        var order = Ranks(a.Key, b.Key, c.Key);

        Assert.Equal([a, b, c], GameWindowList.Managed(windows, None, None, order));
        Assert.Equal([a, b, c], GameWindowList.Accounts(windows, order));
    }

    [Fact]
    public void Une_fenetre_verrouillee_ou_en_onglet_n_est_pas_rangee()
    {
        var free = new FakeGameWindow("A|0|fr.ankama");
        var locked = new FakeGameWindow("B|0|ios");
        var tabbed = new FakeGameWindow("C|0|fr.ankama");
        IGameWindow[] windows = [free, locked, tabbed];
        var order = Ranks(free.Key, locked.Key, tabbed.Key);

        var managed = GameWindowList.Managed(
            windows,
            new HashSet<string>([locked.Key], StringComparer.Ordinal),
            new HashSet<string>([tabbed.Key], StringComparer.Ordinal),
            order);

        Assert.Equal([free], managed);

        // Still accounts: the lock keeps a window from moving, not from being reached.
        Assert.Equal([free, locked, tabbed], GameWindowList.Accounts(windows, order));
    }

    [Fact]
    public void Une_fenetre_morte_n_a_pas_de_touche()
    {
        var alive = new FakeGameWindow("A|0|fr.ankama");
        var dead = new FakeGameWindow("B|0|ios", IsAlive: false);
        var after = new FakeGameWindow("C|0|fr.ankama");
        IGameWindow[] windows = [alive, dead, after];
        var order = Ranks(alive.Key, dead.Key, after.Key);

        // C takes the second key, the dead window none.
        Assert.Equal([alive, after], GameWindowList.Accounts(windows, order));
        Assert.Equal([alive, after], GameWindowList.Managed(windows, None, None, order));
    }

    [Fact]
    public void Une_liste_toute_Android_garde_l_ordre_et_les_exclusions_d_aujourd_hui()
    {
        // ActiveSessions' order: by rank, then by start for the unranked ones.
        var first = new FakeGameWindow("A|0|fr.ankama");
        var locked = new FakeGameWindow("B|0|fr.ankama");
        var tabbed = new FakeGameWindow("C|10|fr.ankama");
        var second = new FakeGameWindow("D|0|fr.ankama");
        var unrankedEarly = new FakeGameWindow("E|0|fr.ankama");
        var unrankedLate = new FakeGameWindow("F|0|fr.ankama");
        IGameWindow[] sessions = [first, locked, tabbed, second, unrankedEarly, unrankedLate];
        var order = Ranks(first.Key, locked.Key, tabbed.Key, second.Key);
        var unmanaged = new HashSet<string>([locked.Key], StringComparer.Ordinal);
        var tabs = new HashSet<string>([tabbed.Key], StringComparer.Ordinal);

        // What ManagedSessions gave: ActiveSessions without the locked and the tabbed.
        var today = sessions.Where(s => !unmanaged.Contains(s.Key) && !tabs.Contains(s.Key)).ToList();

        Assert.Equal(today, GameWindowList.Managed(sessions, unmanaged, tabs, order));
        Assert.Equal(sessions, GameWindowList.Accounts(sessions, order));
    }
}

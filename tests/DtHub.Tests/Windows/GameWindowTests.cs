using DtHub.Core.Windows;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Windows;

public class GameWindowTests
{
    private sealed record FakeGameWindow(
        string Id,
        string Key,
        nint WindowHandle,
        bool IsAlive,
        double SourceAspectRatio,
        string WindowTitle,
        int ProcessId) : IGameWindow;

    private static FakeGameWindow Fake(int n) =>
        new($"ios:{n}", $"U{n}|0|ios", 1000 + n, true, 0, $"iPhone {n}", 1);

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static (WindowManagerService Manager, FakeWindowController Desktop, FakeGameWindow A, FakeGameWindow B) Setup()
    {
        var desktop = new FakeWindowController();
        var a = Fake(1);
        var b = Fake(2);
        desktop.AddWindow(a.WindowHandle, a.ProcessId, a.WindowTitle).AddWindow(b.WindowHandle, b.ProcessId, b.WindowTitle);
        return (new WindowManagerService(desktop, NoDelay), desktop, a, b);
    }

    [Fact]
    public async Task Une_fenetre_qui_n_est_pas_une_session_est_rangee_comme_les_autres()
    {
        var (manager, desktop, a, b) = Setup();

        await manager.TileAsync([a, b]);

        Assert.Contains(desktop.Moves, m => m.Handle == a.WindowHandle);
        Assert.Contains(desktop.Moves, m => m.Handle == b.WindowHandle);
    }

    [Fact]
    public void Une_fenetre_qui_n_est_pas_une_session_recoit_le_focus()
    {
        var (manager, desktop, a, b) = Setup();
        desktop.Foreground = a.WindowHandle;

        var next = manager.FocusNext([a, b]);

        Assert.Same(b, next);
        Assert.Contains(b.WindowHandle, desktop.FocusCalls);
    }

    [Fact]
    public void Une_fenetre_qui_n_est_pas_une_session_est_recoloree_et_retitree()
    {
        var (manager, desktop, a, _) = Setup();

        manager.Recolour([a], _ => 0x123456);
        manager.Retitle([a], w => "T " + w.Key);

        Assert.Equal(0x123456, desktop.FrameColours[a.WindowHandle]);
        Assert.Equal("T U1|0|ios", desktop.Titles[a.WindowHandle]);
    }

    [Fact]
    public async Task Une_fenetre_a_poignee_connue_n_est_pas_cherchee_par_son_processus()
    {
        var (manager, _, a, _) = Setup();

        var handle = await manager.ResolveWindowAsync(a);

        Assert.Equal(a.WindowHandle, handle);
    }

    [Fact]
    public void Une_fenetre_agrandie_qui_n_est_pas_une_session_garde_sa_forme()
    {
        var (manager, desktop, a, b) = Setup();
        var zoomed = a with { SourceAspectRatio = 0.5 };
        var normal = b with { SourceAspectRatio = 0.5 };
        desktop.Zoomed.Add(zoomed.WindowHandle);

        // Twice: a window is corrected once its rectangle has settled.
        manager.EnforceAspect([zoomed, normal]);
        manager.EnforceAspect([zoomed, normal]);

        Assert.DoesNotContain(desktop.Moves, m => m.Handle == zoomed.WindowHandle);
        Assert.Contains(desktop.Moves, m => m.Handle == normal.WindowHandle);
    }

    [Fact]
    public async Task Une_fenetre_morte_qui_n_est_pas_une_session_n_est_pas_cherchee_par_son_processus()
    {
        // An iPhone window being reopened keeps its closed handle a moment, and
        // its process is DT Hub's own: searching it found another of our windows.
        var (manager, _, a, _) = Setup();
        var reopening = a with { WindowHandle = 999 };

        var handle = await manager.ResolveWindowAsync(reopening);

        Assert.Equal(0, handle);
    }
}

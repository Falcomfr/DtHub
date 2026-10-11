using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class CoalescedRunTests
{
    [Fact]
    public async Task Sans_autre_demande_le_travail_tourne_une_fois()
    {
        var run = new CoalescedRun();
        var count = 0;

        await run.RunAsync(() =>
        {
            count++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, count);
        Assert.False(run.IsRunning);
    }

    [Fact]
    public async Task Des_demandes_pendant_le_travail_le_relancent_une_seule_fois()
    {
        // A setting changed while the windows reopen used to be dropped:
        // the reopen running did not carry it, and nothing followed.
        var run = new CoalescedRun();
        var gate = new TaskCompletionSource();
        var count = 0;

        Task Work()
        {
            count++;
            return count == 1 ? gate.Task : Task.CompletedTask;
        }

        var first = run.RunAsync(Work);

        await run.RunAsync(Work);
        await run.RunAsync(Work);

        Assert.True(run.IsRunning);
        Assert.Equal(1, count);

        gate.SetResult();
        await first;

        Assert.Equal(2, count);
        Assert.False(run.IsRunning);
    }

    [Fact]
    public async Task Une_erreur_libere_la_place()
    {
        var run = new CoalescedRun();

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.RunAsync(() => throw new InvalidOperationException()));

        var ran = false;

        await run.RunAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.True(ran);
    }
}

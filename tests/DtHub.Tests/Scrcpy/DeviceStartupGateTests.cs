using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

/// <summary>
/// Two overlapping openings on the same phone break each other. The
/// lock is therefore per device, and two different phones have no
/// reason to wait for one another.
/// </summary>
public sealed class DeviceStartupGateTests
{
    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task Deux_ouvertures_du_meme_appareil_ne_se_chevauchent_jamais()
    {
        using var gate = new DeviceStartupGate(NoDelay);

        var first = await gate.EnterAsync("PHONE-A", CancellationToken.None);
        var second = gate.EnterAsync("PHONE-A", CancellationToken.None);

        Assert.False(second.IsCompleted);

        await first.DisposeAsync();

        await (await second).DisposeAsync();
    }

    [Fact]
    public async Task Deux_appareils_differents_n_attendent_pas_l_un_apres_l_autre()
    {
        using var gate = new DeviceStartupGate(NoDelay);

        await using var a = await gate.EnterAsync("PHONE-A", CancellationToken.None);
        var b = gate.EnterAsync("PHONE-B", CancellationToken.None);

        Assert.True(b.IsCompleted);

        await (await b).DisposeAsync();
    }

    [Fact]
    public async Task Un_appareil_est_annonce_occupe_puis_libre_dans_cet_ordre()
    {
        using var gate = new DeviceStartupGate(NoDelay);

        List<(string Device, bool Busy)> seen = [];
        gate.BusyChanged += (_, e) => seen.Add((e.DeviceId, e.IsBusy));

        var lease = await gate.EnterAsync("PHONE-A", CancellationToken.None);

        Assert.True(gate.IsBusy("PHONE-A"));

        await lease.DisposeAsync();

        Assert.False(gate.IsBusy("PHONE-A"));
        Assert.Equal([("PHONE-A", true), ("PHONE-A", false)], seen);
    }

    [Fact]
    public async Task Le_repos_est_applique_avant_de_rendre_la_place()
    {
        // The next one truly waits, and the busy indicator stays lit
        // for the whole time.
        List<TimeSpan> waits = [];

        using var gate = new DeviceStartupGate((duration, _) =>
        {
            waits.Add(duration);
            return Task.CompletedTask;
        })
        {
            Cooldown = TimeSpan.FromSeconds(2),
        };

        var lease = await gate.EnterAsync("PHONE-A", CancellationToken.None);
        await lease.DisposeAsync();

        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task Une_attente_annulee_ne_laisse_pas_le_verrou_pris()
    {
        using var gate = new DeviceStartupGate(NoDelay);
        using var cancellation = new CancellationTokenSource();

        var held = await gate.EnterAsync("PHONE-A", CancellationToken.None);
        var waiting = gate.EnterAsync("PHONE-A", cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        await held.DisposeAsync();

        var next = gate.EnterAsync("PHONE-A", CancellationToken.None);

        Assert.True(next.IsCompleted);

        await (await next).DisposeAsync();
    }

    [Fact]
    public async Task Un_appareil_sans_identifiant_ne_fait_pas_echouer_la_prise()
    {
        using var gate = new DeviceStartupGate(NoDelay);

        await using var lease = await gate.EnterAsync(string.Empty, CancellationToken.None);

        Assert.True(gate.IsBusy("?"));
    }
}

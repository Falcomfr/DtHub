using DtHub.Core.Almanax;

namespace DtHub.Tests.Almanax;

public class AlmanaxBatchTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static AlmanaxDay Day(DateOnly date, string bonus) =>
        new(date, string.Empty, string.Empty, 2, "Résidu de Solfataré", bonus, string.Empty, string.Empty, string.Empty, string.Empty);

    [Fact]
    public async Task Lire_une_semaine_rend_chaque_jour_dans_l_ordre()
    {
        var dates = Enumerable.Range(0, 7).Select(i => Monday.AddDays(i)).ToList();
        var seen = new List<int>();

        var days = await AlmanaxBatch.ReadAsync(
            dates,
            (date, _) => Task.FromResult<AlmanaxDay?>(Day(date, "Butin")),
            new Progress<int>(seen.Add),
            CancellationToken.None);

        Assert.Equal(dates, days.Select(d => d.Date));
        Assert.All(days, d => Assert.NotNull(d.Day));
    }

    [Fact]
    public async Task Un_jour_illisible_reste_dans_la_liste_sans_journee()
    {
        // The list keeps its seven lines: a day missing from it would
        // look like a day with nothing to bring.
        var days = await AlmanaxBatch.ReadAsync(
            [Monday, Monday.AddDays(1)],
            (date, _) => Task.FromResult(date == Monday ? null : Day(date, "Butin")),
            null,
            CancellationToken.None);

        Assert.Equal(2, days.Count);
        Assert.Null(days[0].Day);
        Assert.NotNull(days[1].Day);
    }

    [Fact]
    public async Task Tout_chercher_rend_chaque_jour_qui_correspond_au_fil_de_la_lecture()
    {
        var found = new List<DateOnly>();

        var days = await AlmanaxBatch.FindAllAsync(
            Monday,
            10,
            day => day.Bonus == "Minerai abondant",
            (date, _) => Task.FromResult<AlmanaxDay?>(Day(date, date.Day % 4 == 0 ? "Minerai abondant" : "Butin")),
            null,
            day => found.Add(day.Date),
            CancellationToken.None);

        Assert.Equal([new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 20)], days.Select(d => d.Date));
        Assert.Equal(days.Select(d => d.Date), found);
    }

    [Fact]
    public async Task Chercher_rend_rien_au_bout_de_l_horizon()
    {
        var reads = 0;

        var found = await AlmanaxBatch.FindAllAsync(
            Monday,
            5,
            day => day.Bonus == "Dremoan",
            (date, _) =>
            {
                reads++;
                return Task.FromResult<AlmanaxDay?>(Day(date, "Butin"));
            },
            null,
            null,
            CancellationToken.None);

        Assert.Empty(found);
        Assert.Equal(5, reads);
    }

    [Fact]
    public async Task Chercher_passe_un_jour_illisible()
    {
        var found = await AlmanaxBatch.FindAllAsync(
            Monday,
            3,
            day => day.Bonus == "Butin",
            (date, _) => Task.FromResult(date == Monday ? null : Day(date, "Butin")),
            null,
            null,
            CancellationToken.None);

        Assert.Equal([Monday.AddDays(1), Monday.AddDays(2)], found.Select(d => d.Date));
    }

    [Fact]
    public async Task Arreter_interrompt_la_lecture()
    {
        using var stop = new CancellationTokenSource();
        var reads = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AlmanaxBatch.FindAllAsync(
            Monday,
            60,
            day => false,
            (date, _) =>
            {
                if (++reads == 2)
                {
                    stop.Cancel();
                }

                return Task.FromResult<AlmanaxDay?>(Day(date, "Butin"));
            },
            null,
            null,
            stop.Token));

        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Chaque_lecture_revient_dans_le_contexte_de_l_appelant()
    {
        // The window's reader drives a WebView2, which only answers on
        // the interface thread. Measured on the real window: the second
        // day of the week was read from a pool thread, and WebView2
        // threw.
        var context = new Context();
        var seen = new List<bool>();
        var previous = SynchronizationContext.Current;

        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            await AlmanaxBatch.ReadAsync(
                [Monday, Monday.AddDays(1), Monday.AddDays(2)],
                async (date, token) =>
                {
                    seen.Add(SynchronizationContext.Current == context);
                    await Task.Delay(1, token).ConfigureAwait(true);
                    return Day(date, "Butin");
                },
                null,
                CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal([true, true, true], seen);
    }

    /// <summary>
    /// A context that runs its posts on the pool, but stays the current
    /// context while doing so, the way the interface thread stays itself.
    /// </summary>
    private sealed class Context : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                d(state);
            });
    }

    [Theory]
    [InlineData("Économie d'ingrédients", "economie")]
    [InlineData("Minerai abondant", "MINERAI")]
    [InlineData("Apparition des ressources et des Archimonstres", "archimonstre")]
    [InlineData("Bonta et Brâkmar", "brakmar")]
    public void La_recherche_ignore_la_casse_et_les_accents(string title, string query)
    {
        Assert.True(AlmanaxReading.Mentions(title, query));
    }

    [Theory]
    [InlineData("Butin", "")]
    [InlineData("Butin", "   ")]
    [InlineData("Butin", null)]
    [InlineData("Butin", "minerai")]
    [InlineData(null, "butin")]
    public void La_recherche_ne_trouve_que_ce_qui_est_ecrit(string? title, string? query)
    {
        Assert.False(AlmanaxReading.Mentions(title, query));
    }
}

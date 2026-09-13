using DtHub.Core.Almanax;

namespace DtHub.Tests.Almanax;

public class AlmanaxRangeTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void Les_bornes_encadrent_le_jour_meme_de_cinq_ans()
    {
        Assert.Equal(new DateOnly(2021, 9, 10), AlmanaxRange.Earliest(Today));
        Assert.Equal(new DateOnly(2031, 9, 10), AlmanaxRange.Latest(Today));
    }

    [Theory]
    [InlineData(2026, 9, 10)]
    [InlineData(2021, 9, 10)]
    [InlineData(2031, 9, 10)]
    [InlineData(2029, 2, 28)]
    public void Une_date_dans_les_bornes_se_consulte(int year, int month, int day)
    {
        Assert.True(AlmanaxRange.Contains(new DateOnly(year, month, day), Today));
    }

    [Theory]
    [InlineData(2021, 9, 9)]
    [InlineData(2031, 9, 11)]
    [InlineData(1, 1, 1)]
    [InlineData(9999, 12, 31)]
    public void Une_date_hors_des_bornes_est_refusee(int year, int month, int day)
    {
        // "0001-01-01" is not just absurd: when asked about it, the
        // portal returns today's date without complaining. The banner
        // would announce year 1 and show today's offering.
        Assert.False(AlmanaxRange.Contains(new DateOnly(year, month, day), Today));
    }

    [Fact]
    public void Une_date_trop_lointaine_revient_au_bord()
    {
        Assert.Equal(new DateOnly(2031, 9, 10), AlmanaxRange.Clamp(new DateOnly(2400, 1, 1), Today));
        Assert.Equal(new DateOnly(2021, 9, 10), AlmanaxRange.Clamp(new DateOnly(1800, 1, 1), Today));
    }

    [Fact]
    public void Une_date_dans_les_bornes_ne_bouge_pas()
    {
        var date = new DateOnly(2027, 3, 28);

        Assert.Equal(date, AlmanaxRange.Clamp(date, Today));
    }

    [Fact]
    public void Le_mois_se_deplace_sans_sortir_des_bornes()
    {
        var month = new DateOnly(2021, 10, 1);

        Assert.Equal(new DateOnly(2021, 9, 1), AlmanaxRange.ShiftMonth(month, -1, Today));
        Assert.Equal(new DateOnly(2021, 9, 1), AlmanaxRange.ShiftMonth(month, -2, Today));
        Assert.Equal(new DateOnly(2021, 9, 1), AlmanaxRange.ShiftMonth(month, -600, Today));
    }

    [Fact]
    public void Le_mois_ne_depasse_pas_la_borne_haute()
    {
        var month = new DateOnly(2031, 8, 1);

        Assert.Equal(new DateOnly(2031, 9, 1), AlmanaxRange.ShiftMonth(month, 1, Today));
        Assert.Equal(new DateOnly(2031, 9, 1), AlmanaxRange.ShiftMonth(month, 40, Today));
    }

    [Fact]
    public void Aucun_deplacement_ne_peut_lever_aux_bornes_de_la_date()
    {
        // DateOnly runs from year 1 to 31 December 9999: stepping back
        // a day from the earliest value throws, and a single arrow
        // click would crash the window. The clamping must make this
        // case unreachable.
        var exception = Record.Exception(() =>
        {
            _ = AlmanaxRange.ShiftMonth(AlmanaxRange.Earliest(Today), -100_000, Today);
            _ = AlmanaxRange.ShiftMonth(AlmanaxRange.Latest(Today), 100_000, Today);
            _ = AlmanaxRange.Clamp(DateOnly.MinValue, Today);
            _ = AlmanaxRange.Clamp(DateOnly.MaxValue, Today);
        });

        Assert.Null(exception);
    }
}

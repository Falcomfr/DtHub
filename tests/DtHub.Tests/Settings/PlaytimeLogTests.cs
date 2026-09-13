using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class PlaytimeLogTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void Un_temps_ajoute_se_retrouve()
    {
        var log = PlaytimeLog.Add(null, Today, 900);

        Assert.Equal(900, PlaytimeLog.Week(log, Today));
        Assert.Equal(900, log["2026-09-10"]);
    }

    [Fact]
    public void Deux_temps_du_meme_jour_s_additionnent()
    {
        var log = PlaytimeLog.Add(PlaytimeLog.Add(null, Today, 900), Today, 600);

        Assert.Equal(1500, PlaytimeLog.Week(log, Today));
    }

    [Fact]
    public void La_semaine_couvre_sept_jours_et_pas_huit()
    {
        var log = PlaytimeLog.Add(null, Today.AddDays(-6), 100);
        log = PlaytimeLog.Add(log, Today, 50);

        Assert.Equal(150, PlaytimeLog.Week(log, Today));

        // The next day, the oldest one falls out of the window.
        Assert.Equal(50, PlaytimeLog.Week(log, Today.AddDays(1)));
    }

    [Fact]
    public void Le_releve_ne_grossit_pas_sans_fin()
    {
        IReadOnlyDictionary<string, int> log = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < 40; i++)
        {
            log = PlaytimeLog.Add(log, Today.AddDays(i), 60);
        }

        Assert.True(log.Count <= PlaytimeLog.Days);
    }

    [Fact]
    public void Une_clef_illisible_s_en_va()
    {
        // The settings file can be edited by hand: a log entry
        // does not have to survive its own corruption.
        var abime = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["2026-09-10"] = 300,
            ["hier"] = 999,
            ["2026-13-45"] = 999,
        };

        var log = PlaytimeLog.Trim(abime, Today);

        Assert.Single(log);
        Assert.Equal(300, PlaytimeLog.Week(log, Today));
    }

    [Fact]
    public void Un_temps_nul_ou_negatif_n_entre_pas()
    {
        Assert.Empty(PlaytimeLog.Add(null, Today, 0));
        Assert.Empty(PlaytimeLog.Add(null, Today, -60));
    }

    [Fact]
    public void Un_jour_a_venir_n_est_pas_compte()
    {
        // A clock set back, a file copied from elsewhere: better to
        // discard it than to return a total that means nothing.
        var log = PlaytimeLog.Add(null, Today.AddDays(3), 600);

        Assert.Equal(0, PlaytimeLog.Week(log, Today));
    }

    [Fact]
    public void Un_releve_vide_ou_absent_ne_leve_pas()
    {
        Assert.Equal(0, PlaytimeLog.Week(null, Today));
        Assert.Empty(PlaytimeLog.Trim(null, Today));
    }

    [Fact]
    public void Le_releve_rendu_est_neuf()
    {
        // The caller writes into a settings document: an in-place
        // modification there would go unnoticed.
        var origine = new Dictionary<string, int>(StringComparer.Ordinal) { ["2026-09-10"] = 60 };

        var log = PlaytimeLog.Add(origine, Today, 60);

        Assert.Equal(60, origine["2026-09-10"]);
        Assert.Equal(120, log["2026-09-10"]);
    }

    [Fact]
    public void La_clef_est_une_date_iso()
    {
        Assert.Equal("2026-09-10", PlaytimeLog.KeyOf(Today));
        Assert.Equal("2026-01-05", PlaytimeLog.KeyOf(new DateOnly(2026, 1, 5)));
    }
}

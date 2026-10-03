using System.Globalization;
using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class PhoneLoadTests
{
    /// <summary>
    /// The shape of <see cref="PhoneLoad.Command" />'s answer: uptime, the
    /// first line of /proc/stat, the game's processes, then kswapd0.
    /// Constructed from the formats of the 13T Pro, figures chosen so the
    /// shares fall on round values.
    /// </summary>
    private static string Output(double uptime, long busy, long idle, long game, long reclaim) =>
        string.Create(CultureInfo.InvariantCulture, $"{uptime:0.00} 1123721.39\r\n")
        + $"cpu  {busy} 0 0 {idle} 0 0 0 0 0 0\r\n"
        + "game\r\n"
        + $"4602 (ama.dofustouch) S 981 981 0 0 -1 1077952832 0 0 0 0 {game} 0 0 0 10 -10 120 0 1\r\n"
        + "reclaim\r\n"
        + $"92 (kswapd0) S 2 0 0 0 -1 2129984 0 0 0 0 {reclaim} 0 0 0 20 0 1 0 1\r\n";

    /// <summary>
    /// Real output, captured on 2026-10-03 on the Xiaomi 13T Pro of the
    /// development machine (Android 16), 25 minutes after a restart and
    /// before the game was launched: three of its seven idle WebView
    /// renderers kept, line breaks as adb hands them.
    /// </summary>
    private const string SortieReelle =
        "1502.19 9732.25\r\n"
        + "cpu  69867 28287 86375 973231 1656 18486 4392 0 0 0\r\n"
        + "game\r\n"
        + "1829 (ocessService0:0) S 2568 979 0 0 -1 1077936448 6150 2068 528 151 15 26 0 19 20 0 29 0 99144 338493026304 38979 18446744073709551615 1 1 0 0 0 0 4612 3 1073775868 0 0 0 17 1 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n"
        + "6338 (ocessService0:0) S 2568 979 0 0 -1 1077936448 37611 2113 2922 86 114 78 0 5 20 0 34 0 2673 338583236608 41672 18446744073709551615 1 1 0 0 0 0 4612 3 1073775868 0 0 0 17 5 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n"
        + "26892 (ocessService0:0) S 2568 979 0 0 -1 1077936448 20723 2131 108 0 126 61 0 6 20 0 31 0 12318 338529538048 40347 18446744073709551615 1 1 0 0 0 0 4612 3 1073775868 0 0 0 17 3 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n"
        + "reclaim\r\n"
        + "92 (kswapd0) S 2 0 0 0 -1 10618944 0 0 0 0 0 4836 0 0 20 0 1 0 22 0 0 18446744073709551615 0 0 0 0 0 0 0 2147483647 0 0 0 0 17 7 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n";

    [Fact]
    public void La_sortie_reelle_se_lit()
    {
        var load = PhoneLoad.Parse(SortieReelle);

        Assert.NotNull(load);
        Assert.Equal(1502.19, load.Uptime.TotalSeconds, 2);
        Assert.Equal(69867 + 28287 + 86375 + 973231 + 1656 + 18486 + 4392, load.TotalTicks);
        Assert.Equal(973231 + 1656, load.IdleTicks);
        Assert.Equal(3, load.Game.Count);
        Assert.Equal(15 + 26, load.Game[1829]);
        Assert.Equal(4836, load.ReclaimTicks);
    }

    [Fact]
    public void Un_second_releve_du_processeur_ne_remplace_pas_le_premier()
    {
        // Seen on the phone: without kswapd0's number, "cat /proc//stat"
        // prints the whole /proc/stat, processor line included.
        var output = SortieReelle + "cpu  1 1 1 1 1 1 1 1 0 0\r\n";

        Assert.Equal(973231 + 1656, PhoneLoad.Parse(output)!.IdleTicks);
    }

    [Fact]
    public void La_sortie_se_lit()
    {
        var load = PhoneLoad.Parse(Output(355423.72, 600, 400, 120, 30));

        Assert.NotNull(load);
        Assert.Equal(355423.72, load.Uptime.TotalSeconds, 2);
        Assert.Equal(1000, load.TotalTicks);
        Assert.Equal(400, load.IdleTicks);
        Assert.Equal(120, load.Game[4602]);
        Assert.Equal(30, load.ReclaimTicks);
    }

    [Fact]
    public void Un_nom_de_processus_avec_espaces_et_parentheses_se_lit()
    {
        // The name sits between the first "(" and the last ")", and it may
        // hold both: the counters are read after the last one.
        var output = Output(10, 600, 400, 0, 0).Replace("(ama.dofustouch)", "(a (b) c)", StringComparison.Ordinal);

        Assert.Equal(0, PhoneLoad.Parse(output)!.Game[4602]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/system/bin/sh: cat: /proc/stat: Permission denied")]
    public void Une_sortie_illisible_ne_donne_rien(string? output)
    {
        Assert.Null(PhoneLoad.Parse(output));
    }

    [Fact]
    public void L_ecart_entre_deux_releves_donne_les_parts()
    {
        // A minute apart: 8 cores at 100 ticks a second make 48 000 ticks,
        // 36 000 of them busy, 12 000 taken by the game, and kswapd0 burned
        // 2 400 ticks, 40 % of one core.
        var before = PhoneLoad.Parse(Output(1000, 100_000, 100_000, 50_000, 1_000))!;
        var after = PhoneLoad.Parse(Output(1060, 136_000, 112_000, 62_000, 3_400))!;

        var strain = PhoneStrain.Between(before, after);

        Assert.NotNull(strain);
        Assert.Equal(0.75, strain.BusyShare, 3);
        Assert.Equal(0.25, strain.GameShare, 3);
        Assert.Equal(0.40, strain.ReclaimCore, 3);
        Assert.Equal(1060, strain.Uptime.TotalSeconds, 2);
    }

    [Fact]
    public void Un_processus_ne_entre_deux_releves_compte_en_entier()
    {
        // A new number is a new process: its counters started from zero
        // within the interval, so all of its ticks belong to it.
        var before = PhoneLoad.Parse(Output(1000, 100_000, 100_000, 50_000, 0))!;
        var after = PhoneLoad.Parse(
            Output(1060, 136_000, 112_000, 12_000, 0).Replace("4602 (", "7777 (", StringComparison.Ordinal))!;

        Assert.Equal(0.25, PhoneStrain.Between(before, after)!.GameShare, 3);
    }

    [Fact]
    public void Un_releve_trop_ancien_ne_sert_pas_de_reference()
    {
        // Two hours apart, the share would be a two-hour average shown as
        // the last minute.
        var before = PhoneLoad.Parse(Output(1000, 100_000, 100_000, 0, 0))!;
        var after = PhoneLoad.Parse(Output(8200, 900_000, 500_000, 0, 0))!;

        Assert.Null(PhoneStrain.Between(before, after));
    }

    [Fact]
    public void Un_seul_mauvais_releve_ne_suffit_pas()
    {
        // A map load or an app switch can fill one minute: only two in a
        // row say the phone stays saturated, and the first calm minute
        // clears it.
        var calme = new PhoneStrain(0.50, 0.20, 0.01, TimeSpan.FromDays(4));
        var sature = new PhoneStrain(0.90, 0.30, 0.38, TimeSpan.FromDays(4));

        Assert.Null(PhoneStrain.Sustained(calme, sature).Describe());
        Assert.Null(PhoneStrain.Sustained(sature, calme).Describe());
        Assert.NotNull(PhoneStrain.Sustained(sature, sature).Describe());
    }

    [Fact]
    public void Un_redemarrage_entre_deux_releves_ne_donne_rien()
    {
        var before = PhoneLoad.Parse(Output(355_000, 100_000, 100_000, 0, 0))!;
        var after = PhoneLoad.Parse(Output(60, 1_000, 1_000, 0, 0))!;

        Assert.Null(PhoneStrain.Between(before, after));
    }
}

using System.Globalization;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestGainTextTests : IDisposable
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr");

    public QuestGainTextTests() => Strings.Speak(Fr);

    public void Dispose()
    {
        Strings.Speak(null);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(12_700_000L, "12,7 M")]
    [InlineData(12_000_000L, "12 M")]
    [InlineData(999_500L, "1 M")]
    [InlineData(999_499L, "999 k")]
    [InlineData(86_400L, "86 k")]
    [InlineData(1_000L, "1 k")]
    [InlineData(950L, "950")]
    [InlineData(0L, "0")]
    public void Le_gain_compact(long value, string expected) =>
        Assert.Equal(expected, QuestGainText.Compact(value));

    [Fact]
    public void La_colonne_suit_le_classement() =>
        Assert.Equal(["2 M", "86 k"], [
            QuestGainText.Column(new QuestGain(2_000_000, 86_000), RewardRanking.Xp),
            QuestGainText.Column(new QuestGain(2_000_000, 86_000), RewardRanking.Kamas)]);

    [Fact]
    public void L_infobulle_d_une_quete_non_faisable_donne_XP_et_kamas() =>
        Assert.Equal($"{1_200L.ToString("N0", Fr)} XP · 35 kamas", QuestGainText.Tip(new QuestGain(1_200, 35)));

    [Fact]
    public void L_infobulle_d_une_quete_faisable_ajoute_le_rang_et_ce_qu_elle_demande() =>
        Assert.Equal(
            $"{1_200L.ToString("N0", Fr)} XP · 35 kamas\nParmi les plus rentables\nDemande : 2 combats, dialogues",
            QuestGainText.Tip(new QuestGain(1_200, 35), 3, ["2 combats", "dialogues"]));

    [Fact]
    public void Sans_rang_ni_raison_l_infobulle_reste_au_gain() =>
        Assert.Equal("10 XP · 0 kamas", QuestGainText.Tip(new QuestGain(10, 0), 0, []));
}

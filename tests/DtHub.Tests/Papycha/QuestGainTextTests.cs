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
    public void Les_deux_colonnes_donnent_l_XP_et_les_kamas_sauf_zero_kamas()
    {
        Assert.Equal(("2 M", "86 k"), QuestGainText.Columns(new QuestGain(2_000_000, 86_000)));
        Assert.Equal(("950", null), QuestGainText.Columns(new QuestGain(950, 0)));
    }

    [Fact]
    public void Zero_XP_leaves_its_column_empty_but_known()
    {
        // Bounties and Nowel stars give no XP in the game's own data: no "0" shown, the kamas column stays.
        Assert.Equal(("", "35"), QuestGainText.Columns(new QuestGain(0, 35)));
        Assert.Equal(("", null), QuestGainText.Columns(new QuestGain(0, 0)));
    }

    [Fact]
    public void L_infobulle_d_une_quete_non_faisable_donne_XP_et_kamas() =>
        Assert.Equal($"{1_200L.ToString("N0", Fr)} XP · 35 kamas", QuestGainText.Tip(new QuestGain(1_200, 35)));

    [Fact]
    public void L_infobulle_d_une_quete_faisable_ajoute_le_rang_et_ce_qu_elle_demande() =>
        Assert.Equal(
            $"{1_200L.ToString("N0", Fr)} XP · 35 kamas\nParmi les plus rentables, surtout pour l'XP\nDemande ≈ 25 min : 2 combats, dialogues",
            QuestGainText.Tip(new QuestGain(1_200, 35), 3, false, new QuestEffort(25, ["2 combats", "dialogues"])));

    [Fact]
    public void L_infobulle_donne_le_temps_meme_sans_raison() =>
        Assert.Equal("10 XP · 0 kamas\nDemande ≈ 1 h 05", QuestGainText.Tip(new QuestGain(10, 0), 0, false, new QuestEffort(65, [])));

    [Fact]
    public void L_infobulle_dit_quand_la_quete_vaut_surtout_pour_les_kamas() =>
        Assert.Equal(
            "10 XP · 35 kamas\nRentable, surtout pour les kamas",
            QuestGainText.Tip(new QuestGain(10, 35), 1, true));

    [Fact]
    public void L_infobulle_d_une_quete_de_plusieurs_etapes_dit_que_c_est_la_recompense_totale() =>
        Assert.Equal(
            "10 XP · 35 kamas\nRécompense totale, 7 étapes\nDemande ≈ 25 min",
            QuestGainText.Tip(new QuestGain(10, 35), 0, false, new QuestEffort(25, []), steps: 7));

    [Fact]
    public void Sans_rang_ni_raison_l_infobulle_reste_au_gain() =>
        Assert.Equal("10 XP · 0 kamas", QuestGainText.Tip(new QuestGain(10, 0), 0, false, null));
}

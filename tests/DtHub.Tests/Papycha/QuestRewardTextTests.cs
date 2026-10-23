using System.Globalization;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestRewardTextTests : IDisposable
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr");
    private static readonly Dictionary<int, string> Names = new() { [1] = "A", [2] = "B" };

    public QuestRewardTextTests() => Strings.Speak(Fr);

    public void Dispose()
    {
        Strings.Speak(null);
        GC.SuppressFinalize(this);
    }

    private static QuestRewardTotal Total(long xp = 1_976_445, long kamas = 11_512, int spells = 0, int titles = 0, params TouchItemReward[] items)
        => new(xp, kamas, items, 0, spells, titles, 0, 0, 0);

    [Fact]
    public void La_ligne_complete()
        => Assert.Equal(
            $"Niv. 191 · bonus +150 % : {1_976_445L.ToString("N0", Fr)} XP · {11_512L.ToString("N0", Fr)} kamas · 10 × A, 1 × B",
            QuestRewardText.Format(Total(items: [new(1, 10), new(2, 1)]), 191, 150, Names));

    [Fact]
    public void Un_bonus_nul_et_zero_kamas_disparaissent()
        => Assert.Equal($"Niv. 5 : {100L.ToString("N0", Fr)} XP",
            QuestRewardText.Format(Total(100, 0), 5, 0, Names));

    [Fact]
    public void Les_autres_gains_suivent_avec_singulier_et_pluriel()
        => Assert.Equal($"Niv. 5 : {100L.ToString("N0", Fr)} XP · 1 sort, 2 titres",
            QuestRewardText.Format(Total(100, 0, 1, 2), 5, 0, Names));

    [Fact]
    public void Un_objet_sans_nom_masque_la_ligne()
        => Assert.Equal("",
            QuestRewardText.Format(Total(100, 0, items: [new(1, 1), new(9, 4)]), 5, 0, Names));

    [Fact]
    public void La_ligne_en_anglais_n_a_pas_d_espace_avant_les_deux_points()
    {
        var en = CultureInfo.GetCultureInfo("en");
        Strings.Speak(en);
        Assert.Equal($"Lvl 191 · bonus +150%: {1_976_445L.ToString("N0", en)} XP · {11_512L.ToString("N0", en)} kamas · 10 × A, 1 × B",
            QuestRewardText.Format(Total(items: [new(1, 10), new(2, 1)]), 191, 150, Names));
    }
}

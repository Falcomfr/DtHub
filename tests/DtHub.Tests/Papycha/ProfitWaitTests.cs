using System.Globalization;

using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class ProfitWaitTests
{
    [Theory]
    [InlineData(TrackerLink.Unknown, false, null, false, false, ProfitWait.ReadingTracker)]
    [InlineData(TrackerLink.NotLinked, true, null, false, false, ProfitWait.ReadingTracker)]
    [InlineData(TrackerLink.Unreadable, false, null, false, false, ProfitWait.TrackerDown)]
    [InlineData(TrackerLink.NotLinked, false, null, false, false, ProfitWait.NeedsLink)]
    [InlineData(TrackerLink.Lost, false, null, false, false, ProfitWait.NeedsLink)]
    [InlineData(TrackerLink.Linked, false, null, false, false, ProfitWait.ReadingGame)]
    [InlineData(TrackerLink.Linked, false, 0, false, false, ProfitWait.ReadingGame)]
    [InlineData(TrackerLink.Linked, false, 0, true, true, ProfitWait.GameDown)]
    [InlineData(TrackerLink.Linked, false, 12, true, false, "")]
    public void Chaque_etat_a_sa_carte(TrackerLink link, bool reading, int? facts, bool factsDone, bool gameFailed, string attendu) =>
        Assert.Equal(attendu, ProfitWait.Of(link, reading, facts, factsDone, gameFailed));

    [Fact]
    public void Seules_les_lectures_tournent_et_seul_le_suivi_se_relance()
    {
        Assert.True(ProfitWait.Spins(ProfitWait.ReadingTracker));
        Assert.True(ProfitWait.Spins(ProfitWait.ReadingGame));
        Assert.False(ProfitWait.Spins(ProfitWait.TrackerDown));
        Assert.True(ProfitWait.CanRetry(ProfitWait.TrackerDown));
        Assert.False(ProfitWait.CanRetry(ProfitWait.GameDown));
    }

    [Theory]
    [InlineData(ProfitWait.ReadingTracker)]
    [InlineData(ProfitWait.ReadingGame)]
    [InlineData(ProfitWait.TrackerDown)]
    [InlineData(ProfitWait.GameDown)]
    [InlineData(ProfitWait.NeedsLink)]
    public void Chaque_carte_a_son_texte_dans_les_trois_langues(string cle) =>
        Assert.All(["fr", "en", "es"], langue => Assert.NotEqual(cle, Strings.GetIn(cle, CultureInfo.GetCultureInfo(langue))));
}

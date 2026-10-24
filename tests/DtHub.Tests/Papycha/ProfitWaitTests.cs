using System.Globalization;

using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class ProfitWaitTests
{
    [Theory]
    [InlineData(TrackerLink.Unknown, false, null, false, ProfitWait.ReadingTracker)]
    [InlineData(TrackerLink.NotLinked, true, null, false, ProfitWait.ReadingTracker)]
    [InlineData(TrackerLink.Unreadable, false, null, false, ProfitWait.TrackerDown)]
    [InlineData(TrackerLink.NotLinked, false, null, false, ProfitWait.NeedsLink)]
    [InlineData(TrackerLink.Lost, false, null, false, ProfitWait.NeedsLink)]
    [InlineData(TrackerLink.Linked, false, null, false, ProfitWait.ReadingGame)]
    [InlineData(TrackerLink.Linked, false, 0, false, ProfitWait.ReadingGame)]
    // A guide's steps read since may have cleared the service's last failure: no facts after a read is "down" still.
    [InlineData(TrackerLink.Linked, false, 0, true, ProfitWait.GameDown)]
    [InlineData(TrackerLink.Linked, false, 12, true, "")]
    public void Chaque_etat_a_sa_carte(TrackerLink link, bool reading, int? facts, bool factsDone, string attendu) =>
        Assert.Equal(attendu, ProfitWait.Of(link, reading, facts, factsDone, viewFailed: false));

    [Fact]
    public void Sans_moteur_la_roue_ne_tourne_pas_sans_fin()
    {
        // The engine failed: no read will ever start, so the failure with "Réessayer" rather than the spinner.
        Assert.Equal(ProfitWait.TrackerDown, ProfitWait.Of(TrackerLink.Unknown, reading: false, null, false, viewFailed: true));
        Assert.Equal(ProfitWait.ReadingTracker, ProfitWait.Of(TrackerLink.Unknown, reading: true, null, false, viewFailed: true));
    }

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

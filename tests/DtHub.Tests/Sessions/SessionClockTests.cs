using DtHub.Core.Sessions;

namespace DtHub.Tests.Sessions;

public class SessionClockTests
{
    [Fact]
    public void Une_fenetre_qui_vient_d_ouvrir_le_dit()
    {
        Assert.Equal("ouvert à l'instant", SessionClock.Describe(TimeSpan.Zero));
    }

    /// <summary>
    /// The boundary, taken from both sides. It is the one that decides
    /// whether the row shows a figure or a sentence, and getting it
    /// wrong by a second would make a window say "1 min" before it had
    /// been open for one.
    /// </summary>
    [Fact]
    public void Le_compte_ne_commence_qu_a_la_minute_pleine()
    {
        Assert.Equal("ouvert à l'instant", SessionClock.Describe(TimeSpan.FromSeconds(59)));
        Assert.Equal("1 min", SessionClock.Describe(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Sous_une_heure_seules_les_minutes_sont_dites()
    {
        Assert.Equal("42 min", SessionClock.Describe(TimeSpan.FromMinutes(42)));
        Assert.Equal("59 min", SessionClock.Describe(TimeSpan.FromSeconds(3599)));
    }

    /// <summary>
    /// The other boundary. Note "1 h 0" and not "1 h 00": the minutes
    /// are not padded, which is exactly what the playtime of the week
    /// already does. A difference between the two would be noticed and
    /// would mean nothing.
    /// </summary>
    [Fact]
    public void A_une_heure_pleine_les_heures_apparaissent()
    {
        Assert.Equal("1 h 0", SessionClock.Describe(TimeSpan.FromSeconds(3600)));
    }

    [Fact]
    public void Au_dela_d_une_heure_les_deux_sont_dits()
    {
        Assert.Equal("1 h 12", SessionClock.Describe(TimeSpan.FromMinutes(72)));
        Assert.Equal("5 h 3", SessionClock.Describe(TimeSpan.FromMinutes(303)));
    }

    /// <summary>
    /// A clock stepped back an hour, or a start read after the machine
    /// resynchronised. It reads as freshly opened, which is wrong by an
    /// hour and harmless, where "-58 min" would be alarming and just as
    /// wrong.
    /// </summary>
    [Fact]
    public void Une_duree_negative_se_lit_comme_une_ouverture()
    {
        Assert.Equal("ouvert à l'instant", SessionClock.Describe(TimeSpan.FromMinutes(-58)));
    }
}

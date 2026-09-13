using DtHub.Core.Users;

namespace DtHub.Tests.Users;

/// <summary>
/// The rule in <see cref="AndroidUserHosting"/> comes from a
/// measurement on a Xiaomi 23078PND5G running Android 15: a linked
/// profile displays on a virtual display, a full user does not.
/// These tests pin down that result so it does not get lost again.
/// </summary>
public class AndroidUserHostingTests
{
    private static AndroidUser User(
        AndroidUserType type,
        bool paused = false,
        int id = 15) =>
        new() { Id = id, Name = "Essai", Type = type, IsPaused = paused };

    [Theory]
    [InlineData(AndroidUserType.Primary)]
    [InlineData(AndroidUserType.ManagedProfile)]
    [InlineData(AndroidUserType.CloneProfile)]
    public void Les_profils_rattaches_peuvent_porter_une_fenetre(AndroidUserType type)
    {
        var verdict = AndroidUserHosting.Describe(User(type));

        Assert.True(verdict.CanHostWindow);
        Assert.Empty(verdict.Reason);
    }

    [Fact]
    public void Un_utilisateur_complet_ne_peut_pas_sur_un_telephone_ordinaire()
    {
        // Measured: "cmd user is-user-visible --display 717 14"
        // returns false, even though "am start" had answered
        // "Status: ok".
        var verdict = AndroidUserHosting.Describe(User(AndroidUserType.Secondary));

        Assert.False(verdict.CanHostWindow);
        Assert.Contains("second espace", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_utilisateur_complet_le_peut_la_ou_l_appareil_l_autorise()
    {
        // Automotive embedded systems answer true to
        // "is-visible-background-users-supported". We do not refuse
        // them what they know how to do.
        var verdict = AndroidUserHosting.Describe(
            User(AndroidUserType.Secondary),
            visibleBackgroundUsers: true);

        Assert.True(verdict.CanHostWindow);
    }

    [Fact]
    public void La_pause_l_emporte_sur_le_type()
    {
        // This is the ordinary state of Shelter and Island. A paused
        // managed profile is still a managed profile, and yet
        // launches nothing.
        var verdict = AndroidUserHosting.Describe(
            User(AndroidUserType.ManagedProfile, paused: true));

        Assert.False(verdict.CanHostWindow);
        Assert.Contains("pause", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AndroidUserType.Guest)]
    [InlineData(AndroidUserType.Restricted)]
    [InlineData(AndroidUserType.Unknown)]
    public void Les_autres_profils_ne_sont_pas_promis(AndroidUserType type)
    {
        var verdict = AndroidUserHosting.Describe(User(type));

        Assert.False(verdict.CanHostWindow);
        Assert.NotEmpty(verdict.Reason);
    }

    [Fact]
    public void Le_refus_dit_toujours_pourquoi()
    {
        // A silent refusal in the interface is no better than a
        // window that does not open: every case must carry a
        // displayable sentence.
        foreach (var type in Enum.GetValues<AndroidUserType>())
        {
            var verdict = AndroidUserHosting.Describe(User(type));

            if (!verdict.CanHostWindow)
            {
                Assert.NotEmpty(verdict.Reason);
            }
        }
    }
}

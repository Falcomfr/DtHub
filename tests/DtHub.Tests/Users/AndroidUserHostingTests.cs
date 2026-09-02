using DtHub.Core.Users;

namespace DtHub.Tests.Users;

/// <summary>
/// La règle de <see cref="AndroidUserHosting"/> vient d'une mesure sur un
/// Xiaomi 23078PND5G sous Android 15 : un profil rattaché s'affiche sur un
/// afficheur virtuel, un utilisateur complet non. Ces tests fixent ce résultat
/// pour qu'il ne se reperde pas.
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
        // Mesuré : « cmd user is-user-visible --display 717 14 » rend faux,
        // alors même que « am start » avait répondu « Status: ok ».
        var verdict = AndroidUserHosting.Describe(User(AndroidUserType.Secondary));

        Assert.False(verdict.CanHostWindow);
        Assert.Contains("second espace", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_utilisateur_complet_le_peut_la_ou_l_appareil_l_autorise()
    {
        // Les systèmes embarqués automobiles répondent vrai à
        // « is-visible-background-users-supported ». On ne leur refuse pas ce
        // qu'ils savent faire.
        var verdict = AndroidUserHosting.Describe(
            User(AndroidUserType.Secondary),
            visibleBackgroundUsers: true);

        Assert.True(verdict.CanHostWindow);
    }

    [Fact]
    public void La_pause_l_emporte_sur_le_type()
    {
        // C'est l'état ordinaire de Shelter et d'Island. Un profil géré en
        // pause reste un profil géré, et ne lance pourtant rien.
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
        // Un refus muet dans l'interface ne vaut pas mieux qu'une fenêtre qui
        // ne s'ouvre pas : chaque cas doit porter une phrase montrable.
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

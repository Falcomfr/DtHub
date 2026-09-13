using DtHub.Core.Sessions;

namespace DtHub.Tests.App;

public class StartupPresenceTests
{
    [Fact]
    public void Le_panneau_reste_masque_quand_des_fenetres_s_ouvrent()
    {
        Assert.False(StartupPresence.ShowConfigurator(remembered: false, openedWindows: 2));
    }

    [Fact]
    public void Le_panneau_reparait_s_il_etait_affiche()
    {
        Assert.True(StartupPresence.ShowConfigurator(remembered: true, openedWindows: 2));
    }

    [Fact]
    public void Sans_aucune_fenetre_le_panneau_parait_meme_s_il_etait_masque()
    {
        // C'est le cas où toutes les instances ont été fermées depuis le
        // panneau : plus rien n'est coché, rien ne s'ouvre, et le masquer
        // laisserait une application sans rien à l'écran ni moyen de la
        // retrouver, les titres des fenêtres étant le seul endroit où le
        // raccourci est rappelé.
        Assert.True(StartupPresence.ShowConfigurator(remembered: false, openedWindows: 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Montrer_avant_le_lancement_ne_mene_jamais_a_reprendre_le_panneau(int ouvertes)
    {
        // C'est toute la sûreté de l'affichage anticipé : si le panneau paraît
        // avant de savoir combien de fenêtres s'ouvriront, il faut qu'aucun
        // nombre de fenêtres ne puisse ensuite le faire disparaître, sans quoi
        // l'utilisateur verrait un panneau clignoter au démarrage.
        foreach (var retenu in new[] { true, false })
        {
            if (StartupPresence.ShowBeforeLaunch(retenu))
            {
                Assert.True(StartupPresence.ShowConfigurator(retenu, ouvertes));
            }
        }
    }

    [Fact]
    public void Un_panneau_masque_a_la_sortie_attend_le_resultat_du_lancement()
    {
        // Masqué à la sortie, il ne reparaît que si rien ne s'ouvre : cela ne
        // se sait qu'après, donc on n'anticipe pas.
        Assert.False(StartupPresence.ShowBeforeLaunch(remembered: false));
    }

    [Fact]
    public void Un_panneau_affiche_a_la_sortie_parait_sans_attendre()
    {
        Assert.True(StartupPresence.ShowBeforeLaunch(remembered: true));
    }
}

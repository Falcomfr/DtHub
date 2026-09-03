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
}

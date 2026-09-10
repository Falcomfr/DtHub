using DtHub.Core.Windows;

namespace DtHub.Tests.App;

public class ShortcutPlacementTests
{
    [Fact]
    public void Une_premiere_installation_pose_le_raccourci_du_bureau()
    {
        Assert.True(ShortcutPlacement.ShouldWriteDesktop(
            desktopLinkExists: false,
            startMenuLinkExists: false));
    }

    [Fact]
    public void Un_raccourci_du_bureau_efface_ne_revient_pas()
    {
        // Le menu Démarrer est là : l'application a déjà démarré ici, donc le
        // bureau vide est un choix, pas un manque.
        Assert.False(ShortcutPlacement.ShouldWriteDesktop(
            desktopLinkExists: false,
            startMenuLinkExists: true));
    }

    [Fact]
    public void Un_raccourci_du_bureau_existant_est_recrit()
    {
        // Sans cela, déplacer l'exécutable laisserait sur le bureau un raccourci
        // qui vise l'ancien emplacement, et rien ne le corrigerait jamais.
        Assert.True(ShortcutPlacement.ShouldWriteDesktop(
            desktopLinkExists: true,
            startMenuLinkExists: true));
    }
}

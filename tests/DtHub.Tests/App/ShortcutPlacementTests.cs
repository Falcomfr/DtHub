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
        // The Start menu is there: the application already started
        // here, so the empty desktop is a choice, not an omission.
        Assert.False(ShortcutPlacement.ShouldWriteDesktop(
            desktopLinkExists: false,
            startMenuLinkExists: true));
    }

    [Fact]
    public void Un_raccourci_du_bureau_existant_est_recrit()
    {
        // Without this, moving the executable would leave a shortcut on
        // the desktop pointing at the old location, and nothing would
        // ever fix it.
        Assert.True(ShortcutPlacement.ShouldWriteDesktop(
            desktopLinkExists: true,
            startMenuLinkExists: true));
    }
}

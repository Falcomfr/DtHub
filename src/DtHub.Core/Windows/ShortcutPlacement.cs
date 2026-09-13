namespace DtHub.Core.Windows;

/// <summary>
/// Decides which shortcuts to write at startup.
///
/// The application is not installed by a setup program: it is a
/// file placed wherever you want. Without a shortcut, you have to go
/// look for it wherever you put it.
/// </summary>
public static class ShortcutPlacement
{
    /// <summary>
    /// True if the desktop shortcut should be written.
    ///
    /// Two needs contradict each other. The shortcut must be placed
    /// without asking anything, otherwise no one gets it. And it
    /// must not be put back after it was just deleted, otherwise the
    /// application forces its presence onto someone's desktop at
    /// every startup.
    ///
    /// The Start menu shortcut, for its part, is always rewritten:
    /// it settles the matter between the two. Its absence signals a
    /// first installation, where both are placed. Its presence
    /// signals an already known installation, where an empty desktop
    /// is a choice that is respected.
    ///
    /// There remains the case of a moved executable: the desktop
    /// shortcut would then point to the old location forever. That
    /// is why an existing shortcut is rewritten, exactly like the
    /// Start menu one.
    /// </summary>
    /// <param name="desktopLinkExists">
    /// A shortcut is already on the desktop.
    /// </param>
    /// <param name="startMenuLinkExists">
    /// A shortcut is already in the Start menu.
    /// </param>
    public static bool ShouldWriteDesktop(bool desktopLinkExists, bool startMenuLinkExists) =>
        desktopLinkExists || !startMenuLinkExists;
}

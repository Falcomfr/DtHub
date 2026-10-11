namespace DtHub.Core.Sessions;

/// <summary>
/// Decides what appears at startup.
///
/// Three rules, which hold together: a game window closed by hand
/// comes back at the next launch, a window closed from the panel does
/// not come back, and the panel shows itself as soon as nothing would
/// be left on screen.
/// </summary>
public static class StartupPresence
{
    /// <summary>
    /// True if the configurator must be shown, knowing whether it was
    /// shown at exit and how many game windows have just opened.
    ///
    /// Without a game window, it is all that remains: hiding it would
    /// leave an application with nothing on screen, not even the
    /// shortcut reminder carried by the window titles. This holds
    /// whether a problem needs to be shown or the startup set is
    /// simply empty, both leading to the same empty screen.
    /// </summary>
    public static bool ShowConfigurator(bool remembered, int openedWindows) =>
        remembered || openedWindows <= 0;

    /// <summary>
    /// True if the configurator can appear without waiting for launch.
    ///
    /// Opening sessions takes several seconds, during which nothing
    /// appeared: the application seemed not to start, even though it
    /// was working. The panel, however, is ready well before them.
    ///
    /// The only thing that prevented showing it right away is that
    /// its presence depends on the number of open windows, which is
    /// only known at the end. Except in one case: when it was shown
    /// at exit, it stays shown no matter what happens next. Showing it
    /// then does not anticipate anything, and therefore cannot lead to
    /// bringing it back on screen.
    ///
    /// In other words, this rule only returns true where
    /// <see cref="ShowConfigurator" /> would return true anyway.
    /// </summary>
    /// <param name="remembered">Was the panel shown at exit.</param>
    public static bool ShowBeforeLaunch(bool remembered) => remembered;

    /// <summary>
    /// True if the panel steps aside after an instance is opened from it,
    /// so the player goes straight to the game without Ctrl+P.
    ///
    /// Only once nothing checked is left to open: launching accounts one
    /// by one would otherwise bring the panel back after each. And never
    /// after a failure, whose banner is the one thing worth reading.
    /// </summary>
    /// <param name="opened">Windows the launch opened.</param>
    /// <param name="problems">Problems it reported.</param>
    /// <param name="checkedNotRunning">
    /// Checked instances, on a connected device, still without a window.
    /// </param>
    public static bool HideAfterLaunch(int opened, int problems, int checkedNotRunning) =>
        opened > 0 && problems == 0 && checkedNotRunning == 0;
}

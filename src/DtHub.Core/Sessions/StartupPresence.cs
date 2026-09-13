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
}

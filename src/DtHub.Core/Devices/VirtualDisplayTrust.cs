namespace DtHub.Core.Devices;

/// <summary>
/// Is the virtual display created by scrcpy unlocked.
///
/// **The defect this type exists to catch.** On a phone that is too old,
/// scrcpy does create its display, announces it, the window opens, and
/// the application says "0 problems". But the display is not unlocked:
/// the system forces the lock screen onto it, and the game cannot launch
/// there. What we see is a clock and a padlock.
///
/// Measured on two devices, same options, same scrcpy version:
///
/// <code>
/// Mi 9T Pro, Android 11: FLAG_ROTATES_WITH_CONTENT, FLAG_PRESENTATION,
///                        FLAG_OWN_CONTENT_ONLY
/// 13T Pro,   Android 16: … FLAG_TRUSTED, FLAG_ALWAYS_UNLOCKED, FLAG_OWN_FOCUS …
/// </code>
///
/// **We measure instead of assuming a version.** The exact API level
/// from which the flag appears depends on Android and the manufacturer,
/// and two devices are not enough to pin it down. The flag itself can be
/// read: it is there or it is not, on the device in front of us.
/// </summary>
public static class VirtualDisplayTrust
{
    /// <summary>
    /// The flag that decides: without it, the display follows the lock
    /// screen.
    /// </summary>
    public const string UnlockedFlag = "FLAG_ALWAYS_UNLOCKED";

    /// <summary>
    /// The flag that decides whether several accounts can live together.
    ///
    /// A display that has its own group has its own top activity.
    /// Without it, all displays share the same one, and the system keeps
    /// only one of them in the foreground: the others move to the cache,
    /// where they lose focus and can be closed.
    ///
    /// Measured, two accounts open on each phone,
    /// <c>dumpsys activity processes</c>:
    ///
    /// <code>
    /// Mi 9T Pro, Android 11: vis+ … u10a260 (vis-activity)
    ///                        cch  … u0a260  (cch-rec)      &lt;- cached
    /// 13T Pro,   Android 16: fg   … u999a475 (top-activity)
    ///                        vis+ … u0a475   (vis-activity) &lt;- both alive
    /// </code>
    /// </summary>
    public const string GroupFlag = "FLAG_OWN_DISPLAY_GROUP";

    /// <summary>
    /// True if scrcpy's virtual display is unlocked, <c>null</c> if it
    /// cannot be found.
    ///
    /// <c>null</c> and not false: no display found means either that
    /// scrcpy did not create one, or that the output changed shape.
    /// Raising an alarm over ignorance would be worse than staying
    /// silent.
    /// </summary>
    public static bool? IsUnlocked(string? dumpsysDisplay) => Flag(dumpsysDisplay, UnlockedFlag);

    /// <summary>
    /// True if the virtual display has its own display group, meaning
    /// this phone can keep several accounts active at the same time.
    /// <c>null</c> when no display is found.
    /// </summary>
    public static bool? HasOwnGroup(string? dumpsysDisplay) => Flag(dumpsysDisplay, GroupFlag);

    private static bool? Flag(string? dumpsysDisplay, string flag)
    {
        if (string.IsNullOrWhiteSpace(dumpsysDisplay))
        {
            return null;
        }

        // The line for scrcpy's display is recognized by its name, which
        // scrcpy gives itself, and by its virtual type.
        foreach (var line in dumpsysDisplay.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("DisplayDeviceInfo{\"scrcpy\"", StringComparison.Ordinal)
                || (line.Contains("uniqueId=\"virtual:", StringComparison.Ordinal)
                    && line.Contains("scrcpy", StringComparison.OrdinalIgnoreCase)))
            {
                return line.Contains(flag, StringComparison.Ordinal);
            }
        }

        return null;
    }
}

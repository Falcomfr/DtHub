namespace DtHub.Core.Devices;

/// <summary>
/// Whether the game is protected from power saving on this device.
///
/// **The flaw this type exists to catch: help instructions that go
/// unfollowed.** The application has long explained that the game
/// needs to be removed from battery restrictions, and that is the
/// leading cause of windows freezing one by one. But it never
/// checked that this had actually been done. Two phones belonging
/// to the same user, recorded on the same day:
///
/// <code>
/// 13T Pro   user,com.ankama.dofustouch,10475   <- prepared
/// Mi 9T Pro (nothing)                          <- never done
/// </code>
///
/// The second is precisely the one that disconnects. The
/// instructions were sound, they simply had not been applied there,
/// and nothing said so.
///
/// **The check knows no brand, and that is deliberate.** It reads
/// Android's own list, not a manufacturer's: the same command
/// answers on all seven device families the help page describes.
/// But the reverse is not guaranteed, and it must be said: the menu
/// path each brand offers does not necessarily write into this
/// list. Measured on Xiaomi, "Battery Saver › No restrictions" does
/// write into it. On Samsung, Honor and vivo, the menu the help
/// page points to is a brand-specific list, which can leave
/// Android's own list empty. The message therefore names the
/// setting that matters to Android, "No restrictions" in the game's
/// battery entry, and refers to the help page for whatever the
/// brand additionally requires.
///
/// **The check is about the device, not the account.** Android
/// keeps its list by package and by application identifier, and a
/// copy of the game in a second profile is a distinct application,
/// with its own restriction. Concluding account by account would
/// require guessing the identifier of each copy. Saying "this phone
/// has never been prepared" is the true statement we can make, and
/// that is already the one that is missing.
/// </summary>
public static class BatteryExemption
{
    /// <summary>
    /// True if the package appears in the list, <c>null</c> when the
    /// answer tells us nothing.
    ///
    /// <c>null</c> rather than false on an empty answer: every
    /// device seen carries dozens of system entries in it, so an
    /// empty list means the command failed, not that nothing is
    /// exempt.
    /// </summary>
    /// <param name="whitelist">
    /// Output of <c>dumpsys deviceidle whitelist</c>.
    /// </param>
    /// <param name="package">Name of the package being searched for.</param>
    public static bool? Covers(string? whitelist, string? package)
    {
        if (string.IsNullOrWhiteSpace(whitelist) || string.IsNullOrWhiteSpace(package))
        {
            return null;
        }

        var found = false;
        var entries = 0;

        foreach (var line in whitelist.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            // Each line is written as "source,package,identifier".
            // The source states who set the exemption, system or
            // user; both protect the same way, and only presence
            // matters.
            var fields = line.Split(',');

            if (fields.Length < 2)
            {
                continue;
            }

            entries++;

            found |= string.Equals(fields[1].Trim(), package.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return entries == 0 ? null : found;
    }
}

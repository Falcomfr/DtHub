namespace DtHub.Core.Devices;

/// <summary>
/// Is the phone locked, right now.
///
/// **Used to avoid crying wolf.** On a device whose virtual
/// display is not trusted, see <see cref="VirtualDisplayTrust" />,
/// the window shows the lock screen instead of the game. But only
/// while the phone is locked: unlocked, the same window shows the
/// game, as measured on a Mi 9T Pro running Android 11.
///
/// The display's flag says what the device is capable of, this one
/// says where it stands. The warning only makes sense if the two
/// agree, otherwise the application announces a fault in front of a
/// game that is displaying fine.
/// </summary>
public static class DeviceLock
{
    /// <summary>
    /// Reads <c>dumpsys trust</c>. Returns <c>null</c> when the
    /// response says nothing: not knowing is not a reason to raise
    /// an alarm.
    ///
    /// The device describes one user per line, and it is the
    /// current user's line that matters: a work profile has its
    /// own line, with no lock state.
    /// </summary>
    public static bool? IsLocked(string? dumpsysTrust)
    {
        if (string.IsNullOrWhiteSpace(dumpsysTrust))
        {
            return null;
        }

        bool? fallback = null;

        foreach (var line in dumpsysTrust.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Read(line) is not { } locked)
            {
                continue;
            }

            if (line.Contains("(current)", StringComparison.Ordinal))
            {
                return locked;
            }

            fallback ??= locked;
        }

        return fallback;
    }

    private static bool? Read(string line)
    {
        const string Marker = "deviceLocked=";

        var at = line.IndexOf(Marker, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var value = line[(at + Marker.Length)..];

        return value.StartsWith('1') ? true : value.StartsWith('0') ? false : null;
    }
}

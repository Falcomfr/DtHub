namespace DtHub.Core.Devices;

/// <summary>
/// Is the phone asleep, and does that cost its game windows their clicks.
///
/// **The defect.** On a Xiaomi Pad 6, Android 14, a player whose tablet
/// went to sleep kept the game's image in DT Hub, but none of his clicks
/// reached it, on every account; lighting the tablet's screen gave them
/// back at once (2026-10-16). On the 13T Pro, Android 16, a click on the
/// virtual display of a phone in "Dozing" opens the page it aims at
/// (measured the same day with scrcpy alone).
/// </summary>
public static class PhoneSleep
{
    /// <summary>
    /// The shell command, filtered on the phone: the whole dump runs to
    /// tens of kilobytes, the line we need is twenty characters.
    /// </summary>
    public static readonly IReadOnlyList<string> Command = ["dumpsys", "power", "|", "grep", "mWakefulness="];

    /// <summary>
    /// True if the phone sleeps, <c>null</c> when the answer names no state.
    /// "Dreaming" is the screensaver, which Android counts as interactive.
    /// </summary>
    public static bool? IsAsleep(string? dump)
    {
        if (string.IsNullOrWhiteSpace(dump))
        {
            return null;
        }

        foreach (var raw in dump.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();

            if (line.StartsWith("mWakefulness=", StringComparison.Ordinal))
            {
                var state = line["mWakefulness=".Length..];
                return state is "Asleep" or "Dozing";
            }
        }

        return null;
    }

    /// <summary>
    /// True for the Android versions whose sleep drops the clicks sent to a
    /// virtual display.
    /// </summary>
    // ponytail: two devices only, 14 drops and 16 does not; Android 15 is
    // left as it was until a player on it says otherwise.
    public static bool DropsClicksWhenAsleep(int? sdk) => sdk is > 0 and <= 34;
}

namespace DtHub.Core.Devices;

/// <summary>
/// Which Android accounts run a package, read from the phone's process list.
///
/// **Why it exists.** A game Android ends, for want of memory or by a crash,
/// left its window black for good: scrcpy keeps showing an empty display, and
/// nothing else told DT Hub the game was gone (2026-10-26).
/// </summary>
public static class GameProcesses
{
    /// <summary>The command, one line per process: its user, then its name.</summary>
    public const string Command = "ps -A -o USER,NAME";

    /// <summary>
    /// The Android user ids running <paramref name="package" />, from users
    /// named "u999_a475": its sub-processes, "package:webview", do not count.
    /// </summary>
    public static IReadOnlySet<int> UsersRunning(string output, string package)
    {
        ArgumentNullException.ThrowIfNull(output);

        HashSet<int> users = [];

        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (parts is [var user, var name, ..]
                && string.Equals(name, package, StringComparison.Ordinal)
                && user.StartsWith('u')
                && user.IndexOf('_', StringComparison.Ordinal) is > 1 and var cut
                && int.TryParse(user.AsSpan(1, cut - 1), out var id))
            {
                _ = users.Add(id);
            }
        }

        return users;
    }
}

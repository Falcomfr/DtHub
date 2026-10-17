using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Dofus;

namespace DtHub.Core.Devices;

/// <summary>
/// Whether the game is playing sound on the phone, and whether scrcpy is
/// taking it for the PC.
///
/// **Why it exists.** The player lost the sound now and then, on the PC and
/// on the phone alike, and relaunching DT Hub brought it back (2026-10-16).
/// Nothing recorded the moment it went, so nothing said why.
///
/// **From the audio policy, not from AudioFlinger.** The policy lists each
/// player with its uid and whether it is active, under its own lock. Dumping
/// AudioFlinger holds the mixer threads while it reads them, and could itself
/// make the sound stutter, the way <c>dumpsys input</c> once made the game
/// lag. Measured on the 13T Pro: 65 ms of the phone's time.
/// </summary>
/// <param name="GameProcesses">Game processes running, every Android profile.</param>
/// <param name="GamePlayers">The game's active audio players.</param>
/// <param name="Captured">An active capture by the shell, which is scrcpy's.</param>
public sealed partial record GameSound(int GameProcesses, int GamePlayers, bool Captured)
{
    /// <summary>The uid of adb's shell, the one scrcpy's server runs under.</summary>
    private const int ShellUid = 2000;

    /// <summary>
    /// The game's uids first, one a line, then the policy's active players.
    /// </summary>
    public const string Command =
        "for p in $(pidof " + DofusPackages.DofusTouch + "); do stat -c %u /proc/$p; done; "
        + "echo players; dumpsys media.audio_policy | grep -E 'uid [0-9]+; State: Active'";

    /// <summary>A game running and not one of its players active.</summary>
    public bool IsSilent => GameProcesses > 0 && GamePlayers == 0;

    /// <summary>
    /// Reads the answer of <see cref="Command" />, or <c>null</c> when it does
    /// not hold the list.
    /// </summary>
    public static GameSound? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var marker = Array.IndexOf(lines, "players");

        if (marker < 0)
        {
            return null;
        }

        HashSet<int> game = [];

        foreach (var line in lines[..marker])
        {
            if (int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
            {
                _ = game.Add(uid);
            }
        }

        // Each port is listed twice, once per output and once per client:
        // counted by its number.
        HashSet<int> players = [];
        var captured = false;

        foreach (var line in lines[(marker + 1)..])
        {
            if (Player().Match(line) is not { Success: true } match)
            {
                continue;
            }

            var port = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var uid = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            if (game.Contains(uid))
            {
                _ = players.Add(port);
            }
            else if (uid == ShellUid)
            {
                captured = true;
            }
        }

        return new GameSound(lines[..marker].Count(l => int.TryParse(l, out _)), players.Count, captured);
    }

    [GeneratedRegex(@"Port ID: (\d+); Session ID: \d+; uid (\d+); State: Active")]
    private static partial Regex Player();
}

using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Brings the requested resolution close to the one that is
/// actually used.
///
/// <see cref="DisplayLadder.For"/> picks the first tier <b>above the
/// window</b>, and the quality ceiling then only ever lowers it. A
/// ceiling chosen beyond the size of the windows therefore changes
/// nothing at all: the same display is requested, at the same cost.
///
/// Without saying so, the interface lets you believe the opposite.
/// Measured on the reference phone, the difference between
/// 1920x1080 and 2560x1440 is worth 0.63 out of 255 in a window 1428
/// tall, for seventy eight percent more bitrate; in a window of
/// 1800, it is worth 3.14 and becomes visible. The right setting
/// therefore depends on the size of the windows, and only that can
/// tell.
/// </summary>
public static partial class DisplayFit
{
    /// <summary>
    /// Resolution actually requested, read from a session's command
    /// line. <c>null</c> if it does not appear there.
    ///
    /// The command line is the only witness that cannot lie: it is
    /// what scrcpy received, not what we believe we gave it.
    /// </summary>
    public static (int Width, int Height)? FromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var match = DisplayPattern().Match(commandLine);

        return match.Success
               && int.TryParse(match.Groups["w"].Value, CultureInfo.InvariantCulture, out var width)
               && int.TryParse(match.Groups["h"].Value, CultureInfo.InvariantCulture, out var height)
               && width > 0 && height > 0
            ? (width, height)
            : null;
    }

    /// <summary>
    /// What is running, and whether the chosen ceiling changes
    /// anything about it.
    ///
    /// Returns an empty sentence when nothing is open: the size of
    /// windows yet to come is not guessed.
    /// </summary>
    /// <param name="chosenHeight">Height ceiling chosen in the panel.</param>
    /// <param name="used">Resolution actually requested from scrcpy.</param>
    public static string Describe(int chosenHeight, (int Width, int Height)? used)
    {
        if (used is not { Width: > 0, Height: > 0 } display)
        {
            return string.Empty;
        }

        var phrase = Strings.Format("DisplayFitSentence", display.Width, display.Height);

        // The ceiling can only lower: above what is actually used,
        // it has no effect, and saying so avoids paying for nothing
        // by raising it.
        return chosenHeight > display.Height
            ? phrase + Strings.Get("DisplayFitCapped")
            : phrase;
    }

    [GeneratedRegex(@"--new-display=(?<w>\d+)x(?<h>\d+)")]
    private static partial Regex DisplayPattern();
}

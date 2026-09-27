using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace DtHub.Core.Touch;

/// <summary>
/// Reads, from <c>dumpsys input</c>, how many fingers Android believes are
/// down on each display.
///
/// **Why it exists.** A player could no longer move their character: the
/// map ignored every click while the interface still answered. Android had
/// two fingers down on that account's display, left there by scrcpy, so
/// every click arrived as a third finger and the game read a multi touch
/// gesture instead of a tap. This reader is how DT Hub now sees it.
/// </summary>
public static partial class TouchStateReader
{
    /// <summary>
    /// The command, filtered on the phone. The full dump weighs about 68 KB
    /// on the development phone; the lines kept here, about 3 KB. Every two
    /// seconds, over the Wi-Fi link that carries the video, that is not a
    /// detail.
    ///
    /// The pattern is quoted for the phone's shell: adb joins its arguments
    /// with spaces and hands the line to <c>sh</c>, which splits the pipe
    /// and strips the quotes.
    /// </summary>
    public static readonly IReadOnlyList<string> Command =
        ["dumpsys", "input", "|", "grep", "-E", "'^[A-Z]|^  [A-Z]|^    [0-9]+ :|touchingPointers|pointerIds'"];

    /// <summary>
    /// Fingers down per display id. A display with nothing down may be
    /// missing or at zero.
    ///
    /// Returns <c>null</c> when the dispatcher's section is not found:
    /// unknown, not "nothing touched". A caller must not conclude a finger
    /// was lifted from a reading that never looked.
    /// </summary>
    public static IReadOnlyDictionary<int, int>? Read(string? dump)
    {
        if (string.IsNullOrEmpty(dump))
        {
            return null;
        }

        var lines = dump.ReplaceLineEndings("\n").Split('\n');

        // The live section only. The same dump carries a frozen copy "at
        // time of last ANR", and MIUI adds its own "MiInput Dispatcher
        // State": reading either would repair, forever, a gesture that
        // ended long ago.
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "Input Dispatcher State:");

        if (start < 0)
        {
            return null;
        }

        var fingers = new Dictionary<int, HashSet<int>>();
        int? display = null;
        var inside = false;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];

            // Back at the margin: the next top level section.
            if (line.Length > 0 && line[0] != ' ')
            {
                break;
            }

            if (!inside)
            {
                if (line.StartsWith("  TouchStatesByDisplay:", StringComparison.Ordinal))
                {
                    inside = true;
                }

                continue;
            }

            // Out of the per display block: the next field of the section.
            if (!line.StartsWith("    ", StringComparison.Ordinal))
            {
                return Count(fingers);
            }

            if (DisplayHeader().Match(line) is { Success: true } header)
            {
                display = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                fingers.TryAdd(display.Value, []);
            }

            if (display is { } id)
            {
                Collect(line, fingers[id]);
            }
        }

        return inside ? Count(fingers) : null;
    }

    /// <summary>
    /// The pointer ids a line holds down, in either of the two ways Android
    /// writes them: named since Android 15, a bit mask before.
    /// </summary>
    private static void Collect(string line, HashSet<int> into)
    {
        foreach (Match touching in TouchingPointers().Matches(line))
        {
            foreach (Match pointer in PointerId().Matches(touching.Groups[1].Value))
            {
                into.Add(int.Parse(pointer.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        foreach (Match mask in PointerMask().Matches(line))
        {
            var bits = uint.Parse(mask.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            for (var bit = 0; bits != 0; bit++, bits >>= 1)
            {
                if ((bits & 1) != 0)
                {
                    into.Add(bit);
                }
            }
        }
    }

    private static Dictionary<int, int> Count(Dictionary<int, HashSet<int>> fingers) =>
        fingers.ToDictionary(pair => pair.Key, pair => pair.Value.Count);

    /// <summary>
    /// A display's own line, four spaces in: "    12 :     Windows:" now,
    /// "    12 : down=true, ..." before. The window lines under it sit deeper.
    /// </summary>
    [GeneratedRegex(@"^    (\d+) :")]
    private static partial Regex DisplayHeader();

    /// <summary>Hovering pointers sit in their own list and are not read.</summary>
    [GeneratedRegex(@"touchingPointers=\[([^\]]*)\]")]
    private static partial Regex TouchingPointers();

    [GeneratedRegex(@"Pointer\(id=(\d+)")]
    private static partial Regex PointerId();

    [GeneratedRegex(@"pointerIds=0x([0-9a-fA-F]+)")]
    private static partial Regex PointerMask();
}

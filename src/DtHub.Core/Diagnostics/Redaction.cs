using System.Text.RegularExpressions;

namespace DtHub.Core.Diagnostics;

/// <summary>
/// Removes from a text whatever identifies a person or their hardware,
/// before it goes out in a report.
///
/// The need is measured, not assumed. Across seven real log files, seven
/// thousand eight hundred and seventy lines, close to three lines in ten
/// carry identifying data: the phone's address and port, its hardware
/// serial number, the Windows username inside a path. Errors, for their
/// part, make up seven lines in a hundred. Sending the log as is would
/// mean delivering everything else just to obtain those.
///
/// The serial number arrives through a path nobody chose: scrcpy prints
/// it in its output, and the application copies that output word for
/// word into its log. That is why the redaction works on the finished
/// text, not at the source.
///
/// <see cref="Processes.ProcessRequest"/> already masks pairing codes,
/// but by exact match: the rule would not catch on "--serial=...", where
/// the value is stuck to its option. Here the redaction works by pattern.
/// </summary>
public static partial class Redaction
{
    /// <summary>What replaces a removed value.</summary>
    public const string Placeholder = "…";

    /// <summary>
    /// Returns the text stripped of anything that identifies.
    /// </summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="secrets">
    /// Values the application knows and that must go wherever they
    /// appear: device serial numbers, addresses recorded, names the
    /// person gave to their accounts.
    /// </param>
    public static string Apply(string? text, IEnumerable<string>? secrets = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Recognizable shapes first. Order matters: redacting a known
        // serial number before recognizing the mDNS name that contains
        // it would break the shape of that name, and the rest of the
        // name would survive. Each pattern therefore takes its whole
        // match before we tackle the pieces.
        var clean = MdnsPattern().Replace(text, Placeholder);

        clean = AddressPattern().Replace(clean, Placeholder);
        clean = SerialOptionPattern().Replace(clean, "--serial=" + Placeholder);
        clean = DisplayPattern().Replace(clean, Placeholder);
        clean = UserPathPattern().Replace(clean, @"C:\Users\" + Placeholder + @"\");

        // Then what the application already knows, by the longest
        // string first, for the same reason: a name contained inside
        // another leaves along with it.
        if (secrets is not null)
        {
            foreach (var secret in secrets
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Trim().Length >= 4)
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s.Length))
            {
                clean = clean.Replace(secret, Placeholder, StringComparison.OrdinalIgnoreCase);
            }
        }

        return clean;
    }

    /// <summary>
    /// The wireless debugging name, "adb-XXXX-YYYY._adb-tls-connect._tcp".
    /// It carries the hardware serial number, the device's stable
    /// identifier, and the codebase already knows it:
    /// <c>MdnsDeviceName.HardwareSerialFrom</c> extracts it from there.
    /// Found ninety-four times in the logs.
    /// </summary>
    [GeneratedRegex(@"adb-[A-Za-z0-9]+-[A-Za-z0-9]+\._adb-tls-[a-z]+\._tcp", RegexOptions.None, 500)]
    private static partial Regex MdnsPattern();

    /// <summary>An IPv4 address, with its port if it carries one.</summary>
    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?\b", RegexOptions.None, 500)]
    private static partial Regex AddressPattern();

    /// <summary>
    /// The value stuck to "--serial=", which the exact match misses.
    /// </summary>
    [GeneratedRegex(@"--serial=\S+", RegexOptions.None, 500)]
    private static partial Regex SerialOptionPattern();

    /// <summary>
    /// A display's device name, "\\.\DISPLAY11". This is a machine
    /// fingerprint with no diagnostic value: the definition and the
    /// position, which remain, say everything a placement needs.
    /// </summary>
    [GeneratedRegex(@"\\\\[.?]\\DISPLAY\d+", RegexOptions.IgnoreCase, 500)]
    private static partial Regex DisplayPattern();

    /// <summary>
    /// The Windows account name inside a path. It shows up without
    /// anyone writing it, through paths under the user's profile: a
    /// hundred and twelve lines in the logs recorded.
    /// </summary>
    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\/:*?""<>|\r\n]+\\", RegexOptions.IgnoreCase, 500)]
    private static partial Regex UserPathPattern();
}

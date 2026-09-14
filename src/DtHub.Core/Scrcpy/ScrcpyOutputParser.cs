using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Nature of a scrcpy refusal. The displayed message is not enough:
/// we also need to know whether a second attempt has a chance of
/// succeeding. Retrying at a more modest resolution fixes a saturated
/// encoder, and fixes nothing at all when the phone is unplugged:
/// that would be thirty more seconds of waiting for the same failure.
/// </summary>
public enum ScrcpyFailureKind
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>
    /// Unrecognized refusal. The resolution is one of its possible
    /// causes.
    /// </summary>
    Unknown,

    /// <summary>
    /// The virtual display never appeared within the allotted time.
    /// </summary>
    Timeout,

    /// <summary>
    /// The video encoder refused the requested resolution or bitrate.
    /// </summary>
    Encoder,

    /// <summary>The device refused to create the virtual display.</summary>
    VirtualDisplayRefused,

    /// <summary>The device was never found.</summary>
    DeviceGone,

    /// <summary>The device disconnected during the session.</summary>
    DeviceDisconnected,

    /// <summary>The device did not authorize this PC.</summary>
    Unauthorized,

    /// <summary>The link with the device could not be established.</summary>
    ConnectionFailed,

    /// <summary>
    /// scrcpy or ADB is missing, or could not start on this PC.
    /// </summary>
    Environment,
}

/// <summary>
/// Reading scrcpy's output. Two pieces of information matter: the
/// identifier of the virtual display it has just created, and the
/// errors to translate for the user.
/// </summary>
public static partial class ScrcpyOutputParser
{
    /// <summary>
    /// Extracts the virtual display's identifier. scrcpy logs it at
    /// creation, in the form
    /// <c>[server] INFO: New display: 1080x1920/320 (id=2)</c>.
    /// </summary>
    public static int? TryParseVirtualDisplayId(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = NewDisplay().Match(line);

        return match.Success
               && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    /// <summary>True if the line is an error reported by scrcpy.</summary>
    public static bool IsError(string? line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains("ERROR:", StringComparison.Ordinal);

    /// <summary>
    /// True if the line announces the end of the session, with or
    /// without the word "ERROR".
    ///
    /// **Measured on a real device**, scrcpy 4.1, Wi-Fi link cut in
    /// the middle of a session by an "adb disconnect":
    ///
    /// <code>
    /// WARN: Device disconnected
    /// </code>
    ///
    /// and then the process stops. The word is not "ERROR", and yet
    /// this is the most frequent failure, the one players complain
    /// about the most. Looking only for "ERROR:" amounted to never
    /// seeing it: the refusal stayed "none", and the session passed
    /// for a clean shutdown, that is, for a window closed by hand.
    ///
    /// The check stays narrow on purpose. scrcpy emits harmless
    /// warnings, and treating them all as failures would reopen
    /// windows that nobody lost.
    /// </summary>
    public static bool IsFatal(string? line)
    {
        if (IsAudio(line))
        {
            return false;
        }

        if (IsError(line))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(line) || !line.Contains("WARN:", StringComparison.Ordinal))
        {
            return false;
        }

        return line.Contains("device disconnected", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if the line speaks about sound and nothing else.
    ///
    /// **Measured on a real device**, scrcpy 4.1, Mi 9T Pro under
    /// Android 11:
    ///
    /// <code>
    /// [server] ERROR: Failed to start audio capture
    /// [server] ERROR: On Android 11, audio capture must be started in the foreground, ...
    /// </code>
    ///
    /// The word is "ERROR", and yet the session opens, mirrors and
    /// keeps going for as long as one likes: scrcpy only gives up
    /// over sound when <c>--require-audio</c> is passed, and
    /// <c>ScrcpyCommandBuilder</c> never passes it. Counted as a
    /// refusal, this startup line settled the failure kind on
    /// <see cref="ScrcpyFailureKind.Unknown" /> for the whole
    /// session, and closing the window by hand hours later was then
    /// read as a link that had dropped. The window reopened by
    /// itself, three times, before the application gave up.
    ///
    /// A phone that goes away while playing announces itself on its
    /// own line, which does not speak about sound and still ends the
    /// session.
    /// </summary>
    private static bool IsAudio(string? line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains("audio", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sorts a scrcpy error line into a category. Recognition is
    /// based on scrcpy's English output, which is not contractual:
    /// anything not recognized becomes
    /// <see cref="ScrcpyFailureKind.Unknown"/>, never a guessed
    /// category.
    /// </summary>
    public static ScrcpyFailureKind Classify(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return ScrcpyFailureKind.None;
        }

        var text = line.ToLowerInvariant();

        if (text.Contains("could not find any adb device", StringComparison.Ordinal)
            || text.Contains("no device found", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.DeviceGone;
        }

        if (text.Contains("device disconnected", StringComparison.Ordinal)
            || text.Contains("connection reset", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.DeviceDisconnected;
        }

        if (text.Contains("unauthorized", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.Unauthorized;
        }

        if (text.Contains("could not create display", StringComparison.Ordinal)
            || text.Contains("virtual display", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.VirtualDisplayRefused;
        }

        if (text.Contains("encoder", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.Encoder;
        }

        if (text.Contains("server connection failed", StringComparison.Ordinal)
            || text.Contains("could not connect", StringComparison.Ordinal))
        {
            return ScrcpyFailureKind.ConnectionFailed;
        }

        return ScrcpyFailureKind.Unknown;
    }

    /// <summary>
    /// Translates a scrcpy error line into an understandable message.
    /// Returns <c>null</c> when the line is not a recognized error, in
    /// which case the caller keeps a generic message and puts the
    /// detail in the log.
    /// </summary>
    public static string? DescribeError(string? line) => Describe(Classify(line));

    /// <summary>Message corresponding to a refusal category.</summary>
    public static string? Describe(ScrcpyFailureKind kind) => kind switch
    {
        ScrcpyFailureKind.DeviceGone => Strings.Get("ScrcpyDeviceGone"),
        ScrcpyFailureKind.DeviceDisconnected => Strings.Get("ScrcpyDisconnected"),
        ScrcpyFailureKind.Unauthorized => Strings.Get("ScrcpyUnauthorized"),
        ScrcpyFailureKind.VirtualDisplayRefused => Strings.Get("ScrcpyNoVirtualDisplay"),
        ScrcpyFailureKind.Encoder => Strings.Get("ScrcpyEncoderRefused"),
        ScrcpyFailureKind.ConnectionFailed => Strings.Get("ScrcpyConnectionFailed"),
        _ => null,
    };

    /// <summary>
    /// True if a second attempt at a more modest resolution has a
    /// chance of succeeding. An unplugged or unauthorized device will
    /// stay that way: insisting would only add a wait to the failure.
    /// </summary>
    public static bool CanRetrySmaller(ScrcpyFailureKind kind) => kind
        is ScrcpyFailureKind.Encoder
        or ScrcpyFailureKind.VirtualDisplayRefused
        or ScrcpyFailureKind.Timeout
        or ScrcpyFailureKind.Unknown;

    /// <summary>
    /// True on the line scrcpy prints when it builds its texture,
    /// which it does on the **first decoded frame** and not before.
    ///
    /// **That is the only moment the window stops being black**, and
    /// nothing measured it. The launch reported the display being
    /// ready and the game's start command returning, both of which
    /// happen seconds before anything is drawn: a window that stayed
    /// black for half a minute left no trace at all, so a slow game
    /// could not be told from a stream that never arrived.
    /// </summary>
    public static bool IsFirstImage(string? line) =>
        !string.IsNullOrWhiteSpace(line) && FirstImage().IsMatch(line);

    [GeneratedRegex(@"New display:.*?\(id=(\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex NewDisplay();

    [GeneratedRegex(@"\bTexture:\s*\d+x\d+", RegexOptions.IgnoreCase)]
    private static partial Regex FirstImage();
}

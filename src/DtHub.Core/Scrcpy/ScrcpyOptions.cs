using System.Globalization;

namespace DtHub.Core.Scrcpy;

/// <summary>Keyboard mode passed to scrcpy.</summary>
public enum ScrcpyKeyboardMode
{
    /// <summary>
    /// Injection through the Android API. Works without any setting on the
    /// phone.
    /// </summary>
    Sdk,

    /// <summary>
    /// Simulated physical keyboard. Better fidelity, requires Android 11 or
    /// later.
    /// </summary>
    Uhid,
}

/// <summary>Mouse mode passed to scrcpy.</summary>
public enum ScrcpyMouseMode
{
    /// <summary>
    /// Injection through the Android API. The PC's cursor stays on the PC.
    /// </summary>
    Sdk,

    /// <summary>
    /// Simulated physical mouse, at kernel level, which does not go through
    /// injection.
    ///
    /// **It captures the PC's mouse**, and this is not a flaw of scrcpy: a
    /// hardware mouse sends relative movements, so the PC loses its cursor for
    /// the duration of the window. A key gives it back, left Alt or one of the
    /// Windows keys. This therefore does not suit anyone juggling several
    /// windows, and suits very well a phone whose overlay refuses injection,
    /// where it is this or nothing.
    /// </summary>
    Uhid,
}

/// <summary>
/// Settings for a scrcpy session. The default values aim for smooth rendering
/// without saturating the network or the phone's encoder when several sessions
/// run at the same time.
/// </summary>
public sealed record ScrcpyOptions
{
    public static readonly ScrcpyOptions Default = new();

    /// <summary>
    /// Frames per second. Beyond this, several sessions saturate the encoder.
    /// </summary>
    public int MaxFps { get; init; } = 45;

    /// <summary>Video bitrate in kilobits per second.</summary>
    public int VideoBitrateKbps { get; init; } = 4000;

    /// <summary>
    /// Delay, in milliseconds, before displaying each frame received.
    ///
    /// Zero by default, as with scrcpy: on a steady link, holding back a frame
    /// would only add delay to the click. The value comes from
    /// <see cref="DtHub.Core.Devices.VideoBuffer" />, which draws it from what
    /// the link is actually worth.
    /// </summary>
    public int VideoBufferMs { get; init; }

    /// <summary>
    /// Audio disabled by default: several simultaneous sessions would produce
    /// an inaudible mix, and audio costs bandwidth.
    /// </summary>
    public bool AudioEnabled { get; init; }

    /// <summary>Two-way clipboard synchronisation.</summary>
    public bool ClipboardSyncEnabled { get; init; } = true;

    /// <summary>
    /// Open each session on its own virtual display. This is what allows
    /// several applications to sit side by side without fighting over the
    /// phone's screen.
    /// </summary>
    public bool UseVirtualDisplay { get; init; } = true;

    /// <summary>
    /// Width of the virtual display, in pixels. Landscape by default: this is
    /// how the game displays, and a portrait display would shrink it to a
    /// strip.
    /// </summary>
    public int VirtualDisplayWidth { get; init; } = 1920;

    /// <summary>
    /// Height of the virtual display, in pixels.
    ///
    /// The image is scaled to the window: this resolution therefore does not
    /// fix the size of the display, but its detail and the scale of the game's
    /// interface. It is chosen at launch by <see cref="DisplayLadder"/>, based
    /// on the window's size.
    ///
    /// Nothing here depends on the device: the virtual display has no relation
    /// to the phone's or tablet's screen. Only the video encoder sets a limit,
    /// a variable one, hence the fallback resolution.
    /// </summary>
    public int VirtualDisplayHeight { get; init; } = 1080;

    /// <summary>
    /// Density of the virtual display. Too low, the Android interface becomes
    /// tiny.
    /// </summary>
    public int VirtualDisplayDpi { get; init; } = 240;

    /// <summary>
    /// Starting from an empty display rather than the device's launcher: we
    /// are the ones who open the wanted application, on the right profile.
    /// </summary>
    public bool DisableVirtualDisplayDecorations { get; init; } = true;

    /// <summary>
    /// Reminder added to the title of each game window, in parentheses. The
    /// windows look alike and overlap: the player must be able to read, above
    /// the image, how to move to the next one.
    /// </summary>
    public string? WindowTitleHint { get; init; }

    /// <summary>
    /// Folder where scrcpy looks for its windows' icon. Otherwise they carry
    /// scrcpy's own icon, which has nothing to do with the application. The
    /// folder must contain a <c>scrcpy.png</c>.
    /// </summary>
    public string? IconDirectory { get; init; }

    public ScrcpyKeyboardMode KeyboardMode { get; init; } = ScrcpyKeyboardMode.Sdk;

    /// <summary>
    /// Mouse mode. Injection by default, which leaves the cursor on the PC.
    /// </summary>
    public ScrcpyMouseMode MouseMode { get; init; } = ScrcpyMouseMode.Sdk;

    /// <summary>
    /// Prefer text input over keycode injection.
    ///
    /// Off: this option swallows modifier keys. Ctrl+V used to type a "v" into
    /// the field instead of pasting, as measured, and scrcpy itself advises
    /// against it for games, where it also breaks the movement keys.
    /// </summary>
    public bool PreferText { get; init; }

    /// <summary>
    /// Pastes Windows's clipboard by typing its content, rather than asking
    /// Android to paste its own.
    ///
    /// As measured: ordinary pasting does nothing. scrcpy does place the text
    /// in the phone's clipboard, the trace confirms it, but the PASTE key it
    /// then sends does not insert anything into the application. Typing the
    /// text bypasses Android's clipboard entirely.
    /// </summary>
    public bool LegacyPaste { get; init; } = true;

    /// <summary>
    /// Prevent the phone's screen from turning off during the session.
    /// </summary>
    public bool KeepDeviceAwake { get; init; } = true;

    /// <summary>Video codec, <c>null</c> to let scrcpy decide.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>
    /// Encoder forced, or <c>null</c> to let the device choose.
    ///
    /// Only forced when the device would put a software encoder ahead of a
    /// hardware one. See <see cref="ScrcpyEncoders.Force" />.
    /// </summary>
    public string? VideoEncoder { get; init; }

    /// <summary>
    /// Asks scrcpy to write its frame rate, one line per second.
    ///
    /// Diagnostics only, off by default. **Zero frames per second is not a
    /// fault**: scrcpy only encodes what changes, and a still screen produces
    /// nothing. That is why this number goes to the log and not into a gauge,
    /// which would raise an alarm for nothing.
    /// </summary>
    public bool PrintFps { get; init; }

    /// <summary>
    /// Codecs that scrcpy 4.1 accepts. A name outside this list makes it exit
    /// right away, and the refusal arrives in a form that nothing can
    /// translate: the user then receives the generic message after the full
    /// timeout, for a mere typo in the settings file.
    ///
    /// The list is not a restriction of our own making: it is the one from
    /// "scrcpy --help". Whether the device can actually encode in the
    /// requested codec remains a separate question, and that one is read from
    /// the output.
    /// </summary>
    private static readonly string[] KnownCodecs = ["h264", "h265", "av1", "vp8", "vp9"];

    /// <summary>Video bitrate in the format expected by scrcpy.</summary>
    public string VideoBitrateArgument =>
        VideoBitrateKbps.ToString(CultureInfo.InvariantCulture) + "K";

    /// <summary>Lowest density accepted.</summary>
    public const int MinDisplayDpi = 60;

    /// <summary>
    /// Highest density accepted.
    ///
    /// This is not an Android limit: measured on the reference phone, the
    /// virtual display accepts 640, 800 and even 1200. This is a caution, and
    /// it must hold the same value everywhere. It was 800 in the calculation
    /// and 640 here, so that any density above 640 was silently shaved down
    /// and the closest zoom would saturate well before the stated height.
    /// </summary>
    public const int MaxDisplayDpi = 800;

    /// <summary>
    /// Returns a corrected copy if aberrant values were entered in the
    /// settings. Correcting is preferred over refusing to start.
    /// </summary>
    public ScrcpyOptions Sanitized() => this with
    {
        MaxFps = Math.Clamp(MaxFps, 1, 240),
        VideoBufferMs = Math.Clamp(VideoBufferMs, 0, 1000),
        VideoBitrateKbps = Math.Clamp(VideoBitrateKbps, 200, 100_000),
        VirtualDisplayWidth = Math.Clamp(VirtualDisplayWidth, 240, 7680),
        VirtualDisplayHeight = Math.Clamp(VirtualDisplayHeight, 240, 7680),
        VirtualDisplayDpi = Math.Clamp(VirtualDisplayDpi, MinDisplayDpi, MaxDisplayDpi),
        VideoCodec = SanitizedCodec(),
    };

    /// <summary>
    /// Codec name if known to scrcpy, <c>null</c> otherwise: better to let
    /// scrcpy choose than to make it fail.
    /// </summary>
    private string? SanitizedCodec()
    {
        if (string.IsNullOrWhiteSpace(VideoCodec))
        {
            return null;
        }

        var value = VideoCodec.Trim().ToLowerInvariant();

        return KnownCodecs.Contains(value, StringComparer.Ordinal) ? value : null;
    }
}

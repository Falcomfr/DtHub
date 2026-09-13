using System.Globalization;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Builds the scrcpy command line. A pure function, fully testable:
/// it is the piece easiest to break without noticing, since a
/// misnamed option only shows up at runtime.
/// </summary>
public static class ScrcpyCommandBuilder
{
    /// <summary>
    /// Title of the game window: the product name followed by the
    /// name chosen by the user. No technical identifier appears in
    /// it, since the window is found by its process.
    /// </summary>
    public static string BuildWindowTitle(string? name, string? hint = null)
    {
        var title = string.IsNullOrWhiteSpace(name)
            ? DtHub.Core.ProductInfo.Name
            : $"{DtHub.Core.ProductInfo.Name} {name.Trim()}";

        return string.IsNullOrWhiteSpace(hint) ? title : $"{title}  ({hint.Trim()})";
    }

    /// <summary>Launch arguments for a mirroring session.</summary>
    /// <param name="serial">ADB serial number of the targeted device.</param>
    /// <param name="windowTitle">
    /// Unique title, used afterwards to find the window again.
    /// </param>
    /// <param name="options">Settings for the session.</param>
    /// <param name="windowPosition">
    /// Initial position and size, if they are known.
    /// </param>
    public static IReadOnlyList<string> BuildMirrorArguments(
        string serial,
        string windowTitle,
        ScrcpyOptions options,
        ScrcpyWindowPlacement? windowPosition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(options);

        var sanitized = options.Sanitized();

        // Every value is joined with "=". Three scrcpy options accept
        // an optional value, including --new-display: for those,
        // getopt only accepts the value when joined, and a value
        // separated by a space is taken for a stray argument. The
        // joined form works in both cases, so it is used everywhere.
        List<string> arguments =
        [
            Option("serial", serial),
            Option("window-title", windowTitle),
            Option("max-fps", sanitized.MaxFps.ToString(CultureInfo.InvariantCulture)),
            Option("video-bit-rate", sanitized.VideoBitrateArgument),
            Option("keyboard", sanitized.KeyboardMode == ScrcpyKeyboardMode.Uhid ? "uhid" : "sdk"),
            Option("mouse", sanitized.MouseMode == ScrcpyMouseMode.Uhid ? "uhid" : "sdk"),

            // Secondary clicks do nothing by default. Otherwise
            // scrcpy binds the right click to BACK: on a display that
            // carries only the game, that back action exits the
            // activity and leaves a black screen. The four actions
            // stay accessible by holding Shift.
            Option("mouse-bind", "----:bhsn"),
        ];

        // --prefer-text swallows the modifiers: alphabetic keys go
        // out as text events, so Ctrl+V typed a "v" into the field
        // instead of pasting. Measured, and scrcpy itself advises
        // against it for games, where it also breaks the movement
        // keys.
        // A buffer is only requested if it is worth something:
        // passing zero would amount to writing scrcpy's own default
        // into the command line.
        if (sanitized.VideoBufferMs > 0)
        {
            arguments.Add(Option(
                "video-buffer",
                sanitized.VideoBufferMs.ToString(CultureInfo.InvariantCulture)));
        }

        if (sanitized.PreferText)
        {
            arguments.Add("--prefer-text");
        }

        if (sanitized.LegacyPaste)
        {
            arguments.Add("--legacy-paste");
        }

        if (!sanitized.AudioEnabled)
        {
            arguments.Add("--no-audio");
        }

        if (!sanitized.ClipboardSyncEnabled)
        {
            arguments.Add("--no-clipboard-autosync");
        }

        if (sanitized.KeepDeviceAwake)
        {
            arguments.Add("--keep-active");
        }

        if (!string.IsNullOrWhiteSpace(sanitized.VideoCodec))
        {
            arguments.Add(Option("video-codec", sanitized.VideoCodec));
        }

        // After the codec, and only when the device would put a
        // software encoder ahead of a hardware one. See
        // "ScrcpyEncoders.Force".
        if (!string.IsNullOrWhiteSpace(sanitized.VideoEncoder))
        {
            arguments.Add(Option("video-encoder", sanitized.VideoEncoder));
        }

        // Diagnostics only: the frame rate goes to the log, not to
        // the screen.
        if (sanitized.PrintFps)
        {
            arguments.Add("--print-fps");
        }

        if (sanitized.UseVirtualDisplay)
        {
            // The display keeps a fixed resolution and the image is
            // scaled to the window. It is the only way to accept any
            // size without losing anything: the game locks the
            // height of its layout at startup and never picks it up
            // again.
            arguments.Add(Option(
                "new-display",
                DisplayArgument(
                    sanitized.VirtualDisplayWidth,
                    sanitized.VirtualDisplayHeight,
                    sanitized.VirtualDisplayDpi)));

            if (sanitized.DisableVirtualDisplayDecorations)
            {
                arguments.Add("--no-vd-system-decorations");
            }
        }

        if (windowPosition is { } placement)
        {
            arguments.AddRange(
            [
                Option("window-x", placement.X.ToString(CultureInfo.InvariantCulture)),
                Option("window-y", placement.Y.ToString(CultureInfo.InvariantCulture)),
            ]);

            arguments.AddRange(
            [
                Option("window-width", placement.Width.ToString(CultureInfo.InvariantCulture)),
                Option("window-height", placement.Height.ToString(CultureInfo.InvariantCulture)),
            ]);
        }

        // Deliberately absent: --kill-adb-on-close. The ADB server is
        // shared with the rest of the machine and must not go down
        // with a DT Hub session.
        return arguments;
    }

    /// <summary>Arguments to fetch the named list of applications.</summary>
    public static IReadOnlyList<string> BuildListAppsArguments(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        return [Option("serial", serial), "--list-apps"];
    }

    /// <summary>
    /// Asks the device for the list of its video encoders.
    ///
    /// scrcpy pushes its server, queries, writes and exits: a few
    /// seconds, no window, no display created.
    /// </summary>
    public static IReadOnlyList<string> BuildListEncodersArguments(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        return [Option("serial", serial), "--list-encoders"];
    }

    /// <summary>A long option and its value, joined together.</summary>
    private static string Option(string name, string value) => $"--{name}={value}";

    /// <summary>
    /// Display resolution in the expected format. The dimensions are
    /// brought down to even numbers: video encoders refuse
    /// odd-numbered sides.
    /// </summary>
    private static string DisplayArgument(int width, int height, int dpi) => string.Create(
        CultureInfo.InvariantCulture,
        $"{Math.Max(2, width - (width % 2))}x{Math.Max(2, height - (height % 2))}/{dpi}");
}

/// <summary>Position and size of a window, in screen pixels.</summary>
public readonly record struct ScrcpyWindowPlacement(int X, int Y, int Width, int Height);

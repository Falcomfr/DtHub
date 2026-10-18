using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Adb;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Users;

namespace DtHub.Core.Sessions;

/// <summary>
/// Opens the game via ADB. The stored component is revalidated if the
/// launch fails: an update may have renamed the main activity. A
/// stopped Android profile is started and then retried.
/// </summary>
public sealed partial class AndroidAppLauncher : IAppLauncher
{
    private readonly IAdbClient _adb;
    private readonly AndroidUserService _users;
    private readonly DofusInstanceService _instances;

    public AndroidAppLauncher(IAdbClient adb, AndroidUserService users, DofusInstanceService instances)
    {
        _adb = adb;
        _users = users;
        _instances = instances;
    }

    public async Task<AppLaunchResult> LaunchAsync(
        string serial,
        int userId,
        string packageName,
        string? knownComponent,
        int? displayId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        if (await PrepareUserAsync(serial, userId, cancellationToken).ConfigureAwait(false)
            is { } refusal)
        {
            return AppLaunchResult.Failure(refusal, Strings.Get("ProfileCannotHostWindow"));
        }

        var first = await TryStartAsync(serial, userId, knownComponent, displayId, cancellationToken)
            .ConfigureAwait(false);

        if (first.Succeeded)
        {
            return first;
        }

        // Under this instance's package name, not the reference one:
        // a copy renamed by the overlay does not resolve under the
        // other one.
        var resolved = await _instances
            .ResolveComponentAsync(serial, userId, packageName, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is null)
        {
            return AppLaunchResult.Failure(
                Strings.Get("GameNotInstalledOnProfile"),
                first.Details);
        }

        if (string.Equals(resolved.Value, knownComponent, StringComparison.Ordinal))
        {
            return first;
        }

        return await TryStartAsync(serial, userId, resolved.Value, displayId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ForceStopAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _adb.ShellAsync(
                serial,
                ["am", "force-stop", "--user", Text(userId), packageName],
                null,
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (AdbException)
        {
            // **A fault here never means the game was not running.**
            // Measured on both phones: "am force-stop" returns 0 for
            // a stopped package, and even for a package that does
            // not exist. It only returns 1 when the command could not
            // reach the device, "device offline" or "device not
            // found".
            //
            // Treating it as harmless was the previous default: the
            // fault used to get lost here, and the game stayed open
            // on the phone after its window closed, with nothing
            // saying so.
            return false;
        }
    }

    /// <summary>
    /// A game mode other than standard can carry a reduced resolution:
    /// "performance" on a player's Galaxy S25 Ultra on 2026-10-17 drew the
    /// game at 75 %. Measured on the 13T Pro, the phone answers "Set game
    /// mode to `STANDARD`" and refuses an unknown package or mode with exit
    /// code 255.
    /// </summary>
    public async Task<bool> SetGameModeAsync(
        string serial,
        int userId,
        string packageName,
        string mode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await _adb.ShellAsync(
                serial,
                ["cmd", "game", "mode", "--user", Text(userId), mode, packageName],
                null,
                cancellationToken).ConfigureAwait(false);

            return output.Contains($"`{mode.ToUpperInvariant()}`", StringComparison.Ordinal);
        }
        catch (AdbException)
        {
            // The refusal is told by the caller, with the way to do it on
            // the phone, which the phone's reason would not give.
            return false;
        }
    }

    /// <summary>
    /// "cmd game list-modes" has no --user: it answers for the main profile
    /// only (13T Pro, 2026-10-18), and neither does "dumpsys game" tell a
    /// profile's mode. The modes offered are the game's, the same on every
    /// profile; the one in use is only known on the main one.
    /// </summary>
    public async Task<GameModes?> ReadGameModesAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await _adb.ShellAsync(
                serial,
                ["cmd", "game", "list-modes", packageName],
                null,
                cancellationToken).ConfigureAwait(false);

            var modes = ParseGameModes(output);

            return userId == 0 ? modes : modes is null ? null : modes with { Current = null };
        }
        catch (AdbException)
        {
            // No game service before Android 13: nothing to choose from,
            // which the settings show by leaving the section out.
            return null;
        }
    }

    /// <summary>
    /// Reads "current mode: performance, available game modes:
    /// [standard,performance,battery,custom]". "custom" is left out: it only
    /// holds what "cmd game set" puts there, nothing a player would pick.
    /// </summary>
    public static GameModes? ParseGameModes(string output)
    {
        var match = GameModesPattern().Match(output);

        if (!match.Success)
        {
            return null;
        }

        string[] available = [.. match.Groups[2].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(m => m != "custom")];

        return available.Length == 0 ? null : new GameModes(match.Groups[1].Value, available);
    }

    [GeneratedRegex(@"current mode: (\w+), available game modes: \[([^\]]*)\]")]
    private static partial Regex GameModesPattern();

    /// <summary>
    /// Prepares the Android profile, or says why it cannot host a
    /// window. Returns <c>null</c> when the way is clear, and
    /// otherwise a showable sentence.
    ///
    /// The check happens here because <c>am start</c> does not do
    /// it: measured on the reference phone, it answers
    /// <c>Status: ok</c> for a full user, then hangs for seventy
    /// seconds without showing anything. Refusing early is better
    /// than a window that never comes.
    /// </summary>
    private async Task<string?> PrepareUserAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken)
    {
        if (userId == 0)
        {
            return null;
        }

        var user = await _users.FindAsync(serial, userId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return null;
        }

        if (AndroidUserHosting.Describe(user) is { CanHostWindow: false } verdict)
        {
            return verdict.Reason;
        }

        if (user.IsRunning)
        {
            return null;
        }

        // The startup result matters: ignoring it left "am start"
        // failing further down, on a message nobody connected back
        // to the profile.
        return await _users.TryStartUserAsync(serial, userId, cancellationToken).ConfigureAwait(false)
            ? null
            : Strings.Format("ProfileCouldNotStart", user.DisplayName);
    }

    /// <summary>
    /// Opens a link on the phone, a Play Store page above all: Android picks
    /// the app that handles it. On a virtual display, the same flag as a game
    /// keeps an empty card out of the phone's recent apps.
    /// </summary>
    public async Task<AppLaunchResult> OpenUriAsync(
        string serial,
        int userId,
        string uri,
        int? displayId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        if (await PrepareUserAsync(serial, userId, cancellationToken).ConfigureAwait(false)
            is { } refusal)
        {
            return AppLaunchResult.Failure(refusal, Strings.Get("ProfileCannotHostWindow"));
        }

        try
        {
            var output = await _adb.ShellAsync(serial, UriArguments(userId, uri, displayId), null, cancellationToken)
                .ConfigureAwait(false);

            // Recorded on the 13T Pro: a link nobody handles is refused with
            // these words, and "am start" still returns zero.
            if (output.Contains("unable to resolve Intent", StringComparison.Ordinal))
            {
                return AppLaunchResult.Failure(Strings.Get("PlayStoreMissing"), output.Trim());
            }

            return Judge(output);
        }
        catch (AdbException exception)
        {
            return AppLaunchResult.Failure(exception.UserMessage, exception.Details);
        }
    }

    /// <summary>The "am start" that opens a link, on a virtual display or not.</summary>
    public static IReadOnlyList<string> UriArguments(int userId, string uri, int? displayId)
    {
        List<string> arguments = ["am", "start", "--user", Text(userId)];

        if (displayId is { } display)
        {
            arguments.AddRange(["--display", Text(display), "--windowingMode", "1", "--activity-exclude-from-recents"]);
        }

        arguments.AddRange(["-a", "android.intent.action.VIEW", "-d", uri]);

        return arguments;
    }

    private async Task<AppLaunchResult> TryStartAsync(
        string serial,
        int userId,
        string? component,
        int? displayId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return AppLaunchResult.Failure(
                Strings.Get("GameNotInstalledOnProfile"),
                "Aucun composant de lancement connu.");
        }

        List<string> arguments = ["am", "start", "--user", Text(userId)];

        if (displayId is { } display)
        {
            arguments.Add("--display");
            arguments.Add(Text(display));

            // Full screen, said outright: left to the system, HyperOS on a
            // tablet (Xiaomi Pad 6, Android 14) opened the game as a floating
            // window inside the virtual display, and the game kept that
            // first size's layout once enlarged, the chat out of view.
            // 1 is WINDOWING_MODE_FULLSCREEN, what phones pick on their own.
            arguments.Add("--windowingMode");
            arguments.Add("1");

            // **On a virtual display only, and that is the key point.**
            //
            // Without this flag, closing a window left an empty
            // thumbnail at the top of the phone's app list. Observed:
            //
            //     pidof com.ankama.dofustouch   -> nothing
            //     No process found for: com.ankama.dofustouch
            //     Recent #0: Task{#63 … sz=0}   <- it stayed
            //
            // The game was indeed closed, but nothing set this card
            // apart from a live application, and tapping it relaunched
            // the game: the user concluded, rightly given what they
            // saw, that closing did not work.
            //
            // Cleaning up after the fact was tried and does not work:
            // once the display is returned, the stack has vanished
            // from "am stack list" and "am stack remove" returns 0
            // without doing anything. The thumbnail must therefore
            // never be born in the first place.
            //
            // Reserved for the virtual display: a window that mirrors
            // the phone's screen shows the game where the user
            // expects to find it in their list.
            arguments.Add("--activity-exclude-from-recents");
        }

        arguments.Add("-n");
        arguments.Add(component);

        try
        {
            var output = await _adb.ShellAsync(serial, arguments, null, cancellationToken)
                .ConfigureAwait(false);

            return Judge(output);
        }
        catch (AdbException exception)
        {
            return AppLaunchResult.Failure(exception.UserMessage, exception.Details);
        }
    }

    private static AppLaunchResult Judge(string output)
    {
        // "am start" returns zero even when it fails: the output is what
        // counts.
        if (output.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Exception", StringComparison.Ordinal))
        {
            // An unknown failure stays unknown. Assuming "application
            // missing" used to send people to reinstall a perfectly present
            // game every time the phone refused for another reason, a
            // permission refusal first and foremost.
            var kind = AdbErrorInterpreter.Classify(output) ?? AdbErrorKind.Unknown;
            return AppLaunchResult.Failure(AdbErrorInterpreter.Describe(kind), output.Trim());
        }

        return AppLaunchResult.Success;
    }

    public async Task<string> ReadGameWindowAsync(
        string serial,
        int displayId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        string activities;

        try
        {
            activities = await _adb.ShellAsync(serial, ["dumpsys", "activity", "activities"], null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AdbException exception)
        {
            // The reason takes the line's place: the caller logs it.
            return exception.Message;
        }

        // One failing, multi-megabyte as they are, must not take the rest of
        // the line down with it.
        var windows = await ReadOrEmptyAsync(serial, ["dumpsys", "window", "windows"], cancellationToken)
            .ConfigureAwait(false);
        var layers = await ReadOrEmptyAsync(serial, ["dumpsys", "SurfaceFlinger"], cancellationToken)
            .ConfigureAwait(false);

        return GameWindowState(activities, windows, layers, displayId, packageName);
    }

    private async Task<string> ReadOrEmptyAsync(
        string serial,
        IReadOnlyList<string> command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _adb.ShellAsync(serial, command, null, cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException)
        {
            // Shows as "?" in the excerpt, which is the report's way to say it.
            return string.Empty;
        }
    }

    /// <summary>
    /// What the phone says of the game's window, in one line: the task's
    /// windowing mode, the bounds it was given, its window's frame, and its
    /// layer's on-screen geometry. The three can disagree: on Xiaomi, a
    /// floating window shrunk by scaling claims the whole display everywhere
    /// but in the layer. "?" for what could not be found.
    /// </summary>
    public static string GameWindowState(
        string activities,
        string windows,
        string layers,
        int displayId,
        string packageName)
    {
        var mode = Section(activities, displayId)
            .Where(l => l.Contains("Task{", StringComparison.Ordinal)
                && l.Contains(":" + packageName + " ", StringComparison.Ordinal))
            .Select(l => ModePattern().Match(l))
            .FirstOrDefault(m => m.Success)?.Groups[1].Value ?? "?";

        var bounds = GameBounds(activities, displayId, packageName) is { } b
            ? string.Create(CultureInfo.InvariantCulture, $"({b.Left}, {b.Top}, {b.Right}, {b.Bottom})")
            : "?";

        string? frame = null;
        string? layer = null;
        var lines = windows.Split('\n');

        // A window's block: its header names the package, the next line its
        // display, then its frame and its surface, whose number is the layer's.
        for (var i = 0; i + 1 < lines.Length && frame is null; i++)
        {
            if (!lines[i].TrimStart().StartsWith("Window #", StringComparison.Ordinal)
                || !lines[i].Contains(" " + packageName + "/", StringComparison.Ordinal)
                || !lines[i + 1].Contains($"mDisplayId={Text(displayId)} ", StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = i + 2; j < lines.Length && !lines[j].TrimStart().StartsWith("Window #", StringComparison.Ordinal); j++)
            {
                frame ??= FramePattern().Match(lines[j]) is { Success: true } f ? f.Value : null;
                layer ??= SurfacePattern().Match(lines[j]) is { Success: true } n ? n.Groups[1].Value : null;
            }
        }

        var geometry = layer is null ? null : LayerGeometry(layers, layer);

        return $"mode={mode}, cadre {bounds}, fenêtre {frame ?? "?"}, couche {geometry ?? "?"}";
    }

    /// <summary>
    /// How much the phone enlarges the game's layer, read from the line of
    /// <see cref="GameWindowState" />, or <c>null</c> when it does not. Above
    /// one, the game draws smaller than its display and the phone stretches
    /// it: a reduced resolution set on the phone, 1.3333 on a player's
    /// Galaxy S25 Ultra on 2026-10-17, blurry under TabDesk too. A Xiaomi
    /// floating window shrunk by scaling stays below one.
    /// </summary>
    public static double? Upscale(string windowState)
    {
        var match = ScalePattern().Match(windowState);

        return match.Success
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            && scale > 1.01
                ? scale
                : null;
    }

    /// <summary>The line after "Layer [n]" that gives its on-screen geometry.</summary>
    private static string? LayerGeometry(string layers, string number)
    {
        var lines = layers.Split('\n');
        var header = $"Layer [{number}]";

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith(header, StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = i + 1; j < Math.Min(lines.Length, i + 4); j++)
            {
                if (GeometryPattern().Match(lines[j]) is { Success: true } g)
                {
                    return g.Value;
                }
            }
        }

        return null;
    }

    /// <summary>The lines of one display's section in "dumpsys activity activities".</summary>
    private static IEnumerable<string> Section(string activities, int displayId)
    {
        var header = $"Display #{Text(displayId)} ";
        var inside = false;

        foreach (var line in activities.Split('\n'))
        {
            if (line.StartsWith("Display #", StringComparison.Ordinal))
            {
                if (inside)
                {
                    yield break;
                }

                inside = line.StartsWith(header, StringComparison.Ordinal);
                continue;
            }

            if (inside)
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// The game's bounds on one display, read from "dumpsys activity
    /// activities": the "CurrentConfiguration" of the game's top activity in
    /// that display's section. That configuration is what the game was given,
    /// so a game held at an old size by Android shows its real bounds there.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom)? GameBounds(
        string dumpsys,
        int displayId,
        string packageName)
    {
        var inGame = false;

        foreach (var line in Section(dumpsys, displayId))
        {
            if (line.Contains("Hist", StringComparison.Ordinal)
                && line.Contains("ActivityRecord{", StringComparison.Ordinal))
            {
                inGame = line.Contains(" " + packageName + "/", StringComparison.Ordinal);
                continue;
            }

            if (inGame && line.TrimStart().StartsWith("CurrentConfiguration=", StringComparison.Ordinal))
            {
                var match = BoundsPattern().Match(line);

                return match.Success
                    ? (Number(match, 1), Number(match, 2), Number(match, 3), Number(match, 4))
                    : null;
            }
        }

        return null;
    }

    private static int Number(Match match, int group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"mBounds=Rect\((-?\d+), (-?\d+) - (-?\d+), (-?\d+)\)")]
    private static partial Regex BoundsPattern();

    [GeneratedRegex(@"\bmode=(\w+)")]
    private static partial Regex ModePattern();

    [GeneratedRegex(@"\bframe=\[[^\]]*\]\[[^\]]*\]")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"mSurface=Surface\(name=[^)]*#(\d+)\)")]
    private static partial Regex SurfacePattern();

    [GeneratedRegex(@"toDisplayTransform=\{ scale x=([0-9.]+)")]
    private static partial Regex ScalePattern();

    // The transform is only written when there is one.
    [GeneratedRegex(@"\bbounds=\{[^}]*\}(?: toDisplayTransform=\{[^}]*\})?")]
    private static partial Regex GeometryPattern();

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

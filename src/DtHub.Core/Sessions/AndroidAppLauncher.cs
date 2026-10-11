using System.Globalization;

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
public sealed class AndroidAppLauncher : IAppLauncher
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

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

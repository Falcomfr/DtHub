using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Users;

namespace DtHub.Core.Apps;

/// <summary>
/// Ouvre une application par ADB. Le composant mémorisé est revalidé si le
/// lancement échoue : une mise à jour de l'application peut avoir renommé son
/// activité principale. Un profil Android arrêté est démarré puis réessayé une
/// fois.
/// </summary>
public sealed class AndroidAppLauncher : IAppLauncher
{
    private readonly IAdbClient _adb;
    private readonly AndroidUserService _users;
    private readonly AppDiscoveryService _apps;

    public AndroidAppLauncher(IAdbClient adb, AndroidUserService users, AppDiscoveryService apps)
    {
        _adb = adb;
        _users = users;
        _apps = apps;
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

        await EnsureUserRunningAsync(serial, userId, cancellationToken).ConfigureAwait(false);

        var component = knownComponent;

        var first = await TryStartAsync(serial, userId, component, displayId, cancellationToken)
            .ConfigureAwait(false);

        if (first.Succeeded)
        {
            return first;
        }

        // Le composant mémorisé peut avoir disparu après une mise à jour de
        // l'application : on le résout à nouveau avant d'abandonner.
        var resolved = await _apps.ResolveLaunchComponentAsync(serial, userId, packageName, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is null)
        {
            return AppLaunchResult.Failure(
                AdbErrorInterpreter.Describe(AdbErrorKind.PackageNotFound),
                first.Details);
        }

        if (string.Equals(resolved.Value, component, StringComparison.Ordinal))
        {
            return first;
        }

        return await TryStartAsync(serial, userId, resolved.Value, displayId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Démarre le profil Android s'il est arrêté. Une application ne peut pas
    /// s'ouvrir sur un profil qui ne tourne pas.
    /// </summary>
    private async Task EnsureUserRunningAsync(string serial, int userId, CancellationToken cancellationToken)
    {
        if (userId == 0)
        {
            return;
        }

        var user = await _users.FindAsync(serial, userId, cancellationToken).ConfigureAwait(false);

        if (user is { IsRunning: false })
        {
            await _users.TryStartUserAsync(serial, userId, cancellationToken).ConfigureAwait(false);
        }
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
                AdbErrorInterpreter.Describe(AdbErrorKind.PackageNotFound),
                "Aucun composant de lancement connu.");
        }

        List<string> arguments =
        [
            "am", "start",
            "--user", userId.ToString(CultureInfo.InvariantCulture),
        ];

        if (displayId is { } display)
        {
            arguments.Add("--display");
            arguments.Add(display.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("-n");
        arguments.Add(component);

        try
        {
            var output = await _adb.ShellAsync(serial, arguments, null, cancellationToken)
                .ConfigureAwait(false);

            // « am start » rend zéro même lorsqu'il échoue : c'est la sortie
            // qui fait foi.
            if (output.Contains("Error", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Exception", StringComparison.Ordinal))
            {
                var kind = AdbErrorInterpreter.Classify(output) ?? AdbErrorKind.PackageNotFound;
                return AppLaunchResult.Failure(AdbErrorInterpreter.Describe(kind), output.Trim());
            }

            return AppLaunchResult.Success;
        }
        catch (AdbException exception)
        {
            return AppLaunchResult.Failure(exception.UserMessage, exception.Details);
        }
    }
}

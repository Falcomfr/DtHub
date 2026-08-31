using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Dofus;
using DtHub.Core.Users;

namespace DtHub.Core.Sessions;

/// <summary>
/// Ouvre le jeu par ADB. Le composant mémorisé est revalidé si le lancement
/// échoue : une mise à jour peut avoir renommé l'activité principale. Un
/// profil Android arrêté est démarré puis réessayé.
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

        await EnsureUserRunningAsync(serial, userId, cancellationToken).ConfigureAwait(false);

        var first = await TryStartAsync(serial, userId, knownComponent, displayId, cancellationToken)
            .ConfigureAwait(false);

        if (first.Succeeded)
        {
            return first;
        }

        // Sous le nom du paquet de cette instance, et non celui de référence :
        // une copie renommée par la surcouche ne se résout pas sous l'autre.
        var resolved = await _instances
            .ResolveComponentAsync(serial, userId, packageName, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is null)
        {
            return AppLaunchResult.Failure(
                "Le jeu n'est plus installé sur ce profil Android.",
                first.Details);
        }

        if (string.Equals(resolved.Value, knownComponent, StringComparison.Ordinal))
        {
            return first;
        }

        return await TryStartAsync(serial, userId, resolved.Value, displayId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ForceStopAsync(
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
        }
        catch (AdbException)
        {
            // L'application n'était peut-être pas lancée : rien à signaler.
        }
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
                "Le jeu n'est plus installé sur ce profil Android.",
                "Aucun composant de lancement connu.");
        }

        List<string> arguments = ["am", "start", "--user", Text(userId)];

        if (displayId is { } display)
        {
            arguments.Add("--display");
            arguments.Add(Text(display));
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

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

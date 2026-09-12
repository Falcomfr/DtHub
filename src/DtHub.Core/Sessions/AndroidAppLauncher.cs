using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
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

        // Sous le nom du paquet de cette instance, et non celui de référence :
        // une copie renommée par la surcouche ne se résout pas sous l'autre.
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
            // **Une faute ici ne dit jamais que le jeu n'était pas lancé.**
            // Mesuré sur les deux téléphones : « am force-stop » répond 0 sur
            // un paquet arrêté, et jusque sur un paquet qui n'existe pas. Il
            // ne rend 1 que lorsque l'ordre n'a pas pu atteindre l'appareil,
            // « device offline » ou « device not found ».
            //
            // Le tenir pour anodin était le défaut : la faute se perdait ici,
            // et le jeu restait ouvert sur le téléphone après la fermeture de
            // sa fenêtre, sans que rien ne le dise.
            return false;
        }
    }

    /// <summary>
    /// Prépare le profil Android, ou dit pourquoi il ne peut pas porter de
    /// fenêtre. Rend <c>null</c> quand la voie est libre, et sinon une phrase
    /// montrable.
    ///
    /// Le contrôle a lieu ici parce qu'<c>am start</c> ne le fait pas :
    /// mesuré sur le téléphone de référence, il répond <c>Status: ok</c> pour
    /// un utilisateur complet, puis pend soixante-dix secondes sans rien
    /// afficher. Refuser tôt vaut mieux qu'une fenêtre qui ne vient jamais.
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

        // Le résultat du démarrage compte : l'ignorer laissait « am start »
        // échouer plus loin, sur un message que personne ne rattachait au
        // profil.
        return await _users.TryStartUserAsync(serial, userId, cancellationToken).ConfigureAwait(false)
            ? null
            : Strings.Format("ProfileCouldNotStart", user.DisplayName);
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

            // **Sur un afficheur virtuel seulement, et c'est la clef.**
            //
            // Sans ce drapeau, fermer une fenêtre laissait une vignette vide en
            // tête de la liste des applications du téléphone. Relevé :
            //
            //     pidof com.ankama.dofustouch   -> rien
            //     No process found for: com.ankama.dofustouch
            //     Recent #0: Task{#63 … sz=0}   <- elle restait
            //
            // Le jeu était bien fermé, mais rien ne distinguait cette carte
            // d'une application vivante, et appuyer dessus relançait le jeu :
            // l'utilisateur en concluait, à raison de ce qu'il voyait, que la
            // fermeture ne marchait pas.
            //
            // Nettoyer après coup a été essayé et ne marche pas : une fois
            // l'afficheur rendu, la pile a disparu de « am stack list » et
            // « am stack remove » répond 0 sans rien faire. Il faut donc que la
            // vignette ne naisse jamais.
            //
            // Réservé à l'afficheur virtuel : une fenêtre qui recopie l'écran
            // du téléphone montre le jeu là où l'utilisateur s'attend à le
            // retrouver dans sa liste.
            arguments.Add("--activity-exclude-from-recents");
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
                // Un échec inconnu reste inconnu. Le supposer « application
                // absente » envoyait réinstaller un jeu bien présent chaque
                // fois que le téléphone refusait pour une autre raison, un
                // refus de permission au premier chef.
                var kind = AdbErrorInterpreter.Classify(output) ?? AdbErrorKind.Unknown;
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

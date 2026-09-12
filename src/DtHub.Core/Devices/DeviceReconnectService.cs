using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>Comment un appareil a été retrouvé, ou pourquoi il ne l'a pas été.</summary>
public enum ReconnectOutcome
{
    /// <summary>Déjà connecté : rien n'a été tenté.</summary>
    AlreadyConnected,

    /// <summary>Reconnecté à la dernière adresse mémorisée.</summary>
    ReconnectedToLastAddress,

    /// <summary>Reconnecté à une adresse découverte par mDNS.</summary>
    ReconnectedByDiscovery,

    /// <summary>Introuvable : téléphone éteint, hors du réseau, ou débogage désactivé.</summary>
    NotFound,

    /// <summary>
    /// Trouvé, joignable, et il refuse ce PC : il ne reconnaît plus sa clé.
    /// Seule une nouvelle association le répare.
    /// </summary>
    RefusedByDevice,
}

/// <summary>
/// Reconnexion automatique au démarrage. L'ordre suit le coût croissant :
/// ce qui est déjà connecté, puis la dernière adresse connue, puis la
/// découverte réseau. L'utilisateur n'a rien à ressaisir.
/// </summary>
public sealed class DeviceReconnectService
{
    private readonly IAdbClient _adb;
    private readonly TimeSpan _directAttempt;

    /// <param name="adb">Le client ADB.</param>
    /// <param name="directAttempt">
    /// Ce qu'on accorde à la dernière adresse connue avant de passer au
    /// balayage. La valeur de service est celle de <see cref="DirectAttempt" />
    /// ; les épreuves la raccourcissent pour ne pas attendre pour de vrai.
    /// </param>
    public DeviceReconnectService(IAdbClient adb, TimeSpan? directAttempt = null) =>
        (_adb, _directAttempt) = (adb, directAttempt ?? DirectAttempt);

    /// <summary>
    /// Ce qu'on accorde à la dernière adresse connue avant de passer au
    /// balayage. Vingt-cinq fois la mesure d'une connexion qui réussit.
    /// </summary>
    private static readonly TimeSpan DirectAttempt = TimeSpan.FromSeconds(5);

    /// <summary>Tente de retrouver un appareil mémorisé.</summary>
    public async Task<ReconnectOutcome> TryReconnectAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (await IsAlreadyConnectedAsync(device, cancellationToken).ConfigureAwait(false))
        {
            return ReconnectOutcome.AlreadyConnected;
        }

        var refused = false;

        // Le port de débogage sans fil change à chaque redémarrage du
        // téléphone : la dernière adresse connue échoue souvent, mais elle est
        // presque gratuite à essayer et évite un balayage mDNS quand elle
        // marche.
        //
        // Presque, et c'est tout l'objet de l'échéance. Mesuré : un appareil
        // qui répond se connecte en 190 ms, un port fermé rend la main en deux
        // secondes, mais un téléphone éteint laisse le système attendre
        // vingt-deux secondes la réponse d'une machine qui ne répondra jamais.
        // La liste des appareils attendait tout ce temps.
        if (device.LastKnownAddress is { Length: > 0 } address && device.LastKnownPort is > 0)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            deadline.CancelAfter(_directAttempt);

            try
            {
                var direct = await _adb
                    .ConnectAsync(address, device.LastKnownPort.Value, deadline.Token)
                    .ConfigureAwait(false);

                if (direct.Succeeded)
                {
                    return ReconnectOutcome.ReconnectedToLastAddress;
                }

                refused = AdbConnectFailure.MeansRefusedKey(direct.FailureReason);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // L'échéance a parlé, pas l'utilisateur : on passe au balayage
                // mDNS, qui sait trouver un appareil dont le port a changé.
            }
        }

        var discovered = await FindByDiscoveryAsync(device, cancellationToken).ConfigureAwait(false);

        if (discovered is null)
        {
            // L'appareil ne s'annonce plus, mais il a refusé ce PC juste
            // avant : c'est le refus qui explique, pas le silence.
            return refused ? ReconnectOutcome.RefusedByDevice : ReconnectOutcome.NotFound;
        }

        var connect = await _adb.ConnectAsync(discovered.Host, discovered.Port, cancellationToken)
            .ConfigureAwait(false);

        if (connect.Succeeded)
        {
            return ReconnectOutcome.ReconnectedByDiscovery;
        }

        return refused || AdbConnectFailure.MeansRefusedKey(connect.FailureReason)
            ? ReconnectOutcome.RefusedByDevice
            : ReconnectOutcome.NotFound;
    }

    /// <summary>
    /// Tente de retrouver plusieurs appareils, tous en même temps.
    ///
    /// **Elles étaient séquentielles, au motif qu'ADB sérialise de toute façon
    /// les connexions. La mesure dit le contraire** : deux connexions vers des
    /// appareils absents prennent 19,3 s ensemble, contre 22 s pour une seule.
    /// Elles ne se gênent pas. En file, chaque téléphone éteint ajoutait son
    /// attente à celle des autres, et la liste des appareils attendait la
    /// somme.
    ///
    /// Tous les appareils connus sont tentés, sans filtre préalable : la
    /// sûreté vient de la correspondance entre le numéro de série et le nom du
    /// service annoncé, pas d'un drapeau qui peut manquer sur un appareil
    /// mémorisé par une version antérieure.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ReconnectOutcome>> TryReconnectAllAsync(
        IEnumerable<AndroidDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        cancellationToken.ThrowIfCancellationRequested();

        var attempts = devices
            .Select(async device =>
            {
                try
                {
                    return (device.Id, Outcome: await TryReconnectAsync(device, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (AdbException)
                {
                    // Un appareil injoignable ne doit pas interrompre la
                    // reprise des autres.
                    return (device.Id, Outcome: ReconnectOutcome.NotFound);
                }
            })
            .ToList();

        var results = await Task.WhenAll(attempts).ConfigureAwait(false);

        var outcomes = new Dictionary<string, ReconnectOutcome>(StringComparer.Ordinal);

        foreach (var (id, outcome) in results)
        {
            outcomes[id] = outcome;
        }

        return outcomes;
    }

    private async Task<bool> IsAlreadyConnectedAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        var entries = await _adb.ListDevicesAsync(detailed: false, cancellationToken).ConfigureAwait(false);

        return entries.Any(e => e.IsReady
            && (string.Equals(e.Serial, device.Serial, StringComparison.Ordinal)
                || string.Equals(e.Serial, device.ReconnectAddress, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Cherche l'annonce mDNS de l'appareil. Le nom du service contient le
    /// numéro de série matériel, ce qui permet de ne pas se connecter au
    /// téléphone du voisin.
    /// </summary>
    private async Task<MdnsService?> FindByDiscoveryAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        var services = await _adb.ListMdnsServicesAsync(cancellationToken).ConfigureAwait(false);
        var connectServices = services.Where(s => s.IsConnect).ToList();

        var byIdentity = connectServices.FirstOrDefault(s => s.MatchesSerial(device.Id));
        if (byIdentity is not null)
        {
            return byIdentity;
        }

        var bySerial = connectServices.FirstOrDefault(s => s.MatchesSerial(device.Serial));
        if (bySerial is not null)
        {
            return bySerial;
        }

        // Repli : même hôte que la dernière fois, port différent. C'est le cas
        // courant après un redémarrage du téléphone sur un réseau à baux fixes.
        return device.LastKnownAddress is { Length: > 0 } address
            ? connectServices.FirstOrDefault(s => string.Equals(s.Host, address, StringComparison.Ordinal))
            : null;
    }
}

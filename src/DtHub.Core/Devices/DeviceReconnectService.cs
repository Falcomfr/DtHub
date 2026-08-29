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
}

/// <summary>
/// Reconnexion automatique au démarrage. L'ordre suit le coût croissant :
/// ce qui est déjà connecté, puis la dernière adresse connue, puis la
/// découverte réseau. L'utilisateur n'a rien à ressaisir.
/// </summary>
public sealed class DeviceReconnectService
{
    private readonly IAdbClient _adb;

    public DeviceReconnectService(IAdbClient adb) => _adb = adb;

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

        // Le port de débogage sans fil change à chaque redémarrage du
        // téléphone : la dernière adresse connue échoue souvent, mais elle est
        // gratuite à essayer et évite un balayage mDNS quand elle marche.
        if (device.LastKnownAddress is { Length: > 0 } address && device.LastKnownPort is > 0)
        {
            var direct = await _adb.ConnectAsync(address, device.LastKnownPort.Value, cancellationToken)
                .ConfigureAwait(false);

            if (direct.Succeeded)
            {
                return ReconnectOutcome.ReconnectedToLastAddress;
            }
        }

        var discovered = await FindByDiscoveryAsync(device, cancellationToken).ConfigureAwait(false);
        if (discovered is null)
        {
            return ReconnectOutcome.NotFound;
        }

        var connect = await _adb.ConnectAsync(discovered.Host, discovered.Port, cancellationToken)
            .ConfigureAwait(false);

        return connect.Succeeded ? ReconnectOutcome.ReconnectedByDiscovery : ReconnectOutcome.NotFound;
    }

    /// <summary>
    /// Tente de retrouver plusieurs appareils. Les tentatives sont
    /// séquentielles : ADB sérialise de toute façon les connexions, et cela
    /// évite d'empiler les balayages mDNS.
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

        var outcomes = new Dictionary<string, ReconnectOutcome>(StringComparer.Ordinal);

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                outcomes[device.Id] = await TryReconnectAsync(device, cancellationToken).ConfigureAwait(false);
            }
            catch (AdbException)
            {
                // Un appareil injoignable ne doit pas interrompre la reprise
                // des autres.
                outcomes[device.Id] = ReconnectOutcome.NotFound;
            }
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

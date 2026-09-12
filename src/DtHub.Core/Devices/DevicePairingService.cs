using DtHub.Core.Adb;
using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// Ce que la reprise des appareils annoncés a donné.
/// </summary>
/// <param name="Connected">Adresses désormais connectées.</param>
/// <param name="Refused">
/// Numéros de série des appareils qui s'annonçaient et ont refusé la
/// connexion. Un refus sur une annonce fraîche ne s'explique pas par une
/// adresse périmée : l'appareil ne reconnaît plus la clé de ce PC.
/// </param>
public sealed record AnnouncedConnections(
    IReadOnlyList<string> Connected,
    IReadOnlyList<string> Refused);

/// <summary>
/// Conduit l'appairage du débogage sans fil de bout en bout : appairage avec
/// le code affiché par le téléphone, découverte du port de connexion par mDNS,
/// puis connexion. L'utilisateur ne tape jamais de commande ADB.
/// </summary>
public sealed class DevicePairingService
{
    private readonly IAdbClient _adb;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">
    /// Attente entre deux sondages mDNS. Injectable pour que les tests
    /// n'attendent pas réellement.
    /// </param>
    public DevicePairingService(IAdbClient adb, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _adb = adb;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>
    /// Durée pendant laquelle on attend l'annonce mDNS du port de connexion.
    /// Le téléphone met quelques secondes à la publier après l'appairage.
    /// </summary>
    public TimeSpan ConnectDiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(12);

    public TimeSpan DiscoveryPollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Appaire puis connecte. Le code d'appairage n'est ni journalisé, ni
    /// conservé au-delà de l'appel.
    /// </summary>
    public async Task<WirelessPairingResult> PairAndConnectAsync(
        string host,
        int pairingPort,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        var pairing = await _adb.PairAsync(host, pairingPort, pairingCode, cancellationToken)
            .ConfigureAwait(false);

        if (!pairing.Succeeded)
        {
            return new WirelessPairingResult(
                WirelessPairingStatus.PairingFailed,
                AdbErrorInterpreter.Describe(AdbErrorKind.PairingFailed));
        }

        var service = await WaitForConnectServiceAsync(host, cancellationToken).ConfigureAwait(false);

        if (service is null)
        {
            return new WirelessPairingResult(
                WirelessPairingStatus.ConnectPortNotFound,
                Strings.Get("PairedButPortUnknown"),
                DeviceGuid: pairing.DeviceGuid);
        }

        var connect = await _adb.ConnectAsync(service.Host, service.Port, cancellationToken)
            .ConfigureAwait(false);

        return connect.Succeeded
            ? new WirelessPairingResult(
                WirelessPairingStatus.Connected,
                Strings.Get("PairedAndConnected"),
                service.Address,
                pairing.DeviceGuid)
            : new WirelessPairingResult(
                WirelessPairingStatus.ConnectFailed,
                AdbErrorInterpreter.Describe(AdbErrorKind.ConnectionFailed),
                service.Address,
                pairing.DeviceGuid);
    }

    /// <summary>
    /// Connexion directe à une adresse, pour le cas où l'utilisateur saisit
    /// lui-même le port parce que le mDNS est bloqué.
    /// </summary>
    public async Task<WirelessPairingResult> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var connect = await _adb.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

        return connect.Succeeded
            ? new WirelessPairingResult(WirelessPairingStatus.Connected, Strings.Get("PhoneConnected"), $"{host}:{port}")
            : new WirelessPairingResult(
                WirelessPairingStatus.ConnectFailed,
                AdbErrorInterpreter.Describe(AdbErrorKind.ConnectionFailed),
                $"{host}:{port}");
    }

    /// <summary>
    /// Sonde le mDNS jusqu'à voir le service de connexion annoncé par l'hôte
    /// donné, ou jusqu'à expiration du délai.
    /// </summary>
    public async Task<MdnsService?> WaitForConnectServiceAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + ConnectDiscoveryTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var services = await _adb.ListMdnsServicesAsync(cancellationToken).ConfigureAwait(false);

            var match = services.FirstOrDefault(
                s => s.IsConnect && string.Equals(s.Host, host, StringComparison.Ordinal));

            if (match is not null)
            {
                return match;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return null;
            }

            await _delay(DiscoveryPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Cherche les téléphones en attente d'appairage sur le réseau, pour
    /// pré-remplir l'assistant plutôt que de faire recopier une adresse.
    /// </summary>
    public async Task<IReadOnlyList<MdnsService>> FindPairingCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var services = await _adb.ListMdnsServicesAsync(cancellationToken).ConfigureAwait(false);

        return [.. services.Where(s => s.IsPairing)];
    }

    /// <summary>
    /// Téléphones qui annoncent être joignables sur le réseau. Un téléphone
    /// déjà associé à ce PC s'y connectera sans code : c'est ce qui permet de
    /// proposer une connexion en un clic, voire de la tenter d'office.
    /// </summary>
    public async Task<IReadOnlyList<MdnsService>> FindConnectableAsync(
        CancellationToken cancellationToken = default)
    {
        var services = await _adb.ListMdnsServicesAsync(cancellationToken).ConfigureAwait(false);

        return [.. services.Where(s => s.IsConnect)];
    }

    /// <summary>
    /// Tente de connecter tout ce qui s'annonce sur le réseau. La tentative
    /// n'aboutit que pour les téléphones déjà associés à ce PC : ADB conserve
    /// la clé d'association, et refuse les autres. Il n'y a donc aucun risque
    /// de se connecter au téléphone d'un voisin.
    /// </summary>
    /// <returns>Adresses effectivement connectées.</returns>
    /// <param name="discarded">
    /// Appareils dont l'association a été rompue, par identifiant matériel. Une
    /// annonce ne suffit pas à revenir : ADB garde sa clé et se reconnecterait
    /// sans fin à un téléphone qu'on vient d'écarter, dans le rafraîchissement
    /// même que déclenche le bouton de rupture.
    /// </param>
    public async Task<AnnouncedConnections> ConnectAnnouncedAsync(
        IReadOnlyCollection<string> alreadyConnected,
        IReadOnlySet<string>? discarded = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alreadyConnected);

        List<string> connected = [];
        List<string> refused = [];

        foreach (var service in await FindConnectableAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (alreadyConnected.Contains(service.Address))
            {
                continue;
            }

            // Le nom de l'annonce porte le numéro de série du téléphone, qui
            // est aussi son identifiant chez nous : c'est ce qui permet de
            // reconnaître un appareil écarté malgré un changement d'adresse.
            if (discarded is { Count: > 0 }
                && MdnsDeviceName.HardwareSerialFromInstance(service.Name) is { Length: > 0 } serial
                && discarded.Contains(serial))
            {
                continue;
            }

            var result = await _adb.ConnectAsync(service.Host, service.Port, cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                connected.Add(service.Address);

                continue;
            }

            // **Une annonce qui refuse la connexion est un fait, pas un
            // hasard.** L'appareil dit lui-même, à l'instant, sur quel port il
            // écoute : l'adresse ne peut pas être périmée. S'il refuse quand
            // même, c'est qu'il ne reconnaît plus la clé de ce PC, et seule
            // une nouvelle association la lui redonnera.
            if (MdnsDeviceName.HardwareSerialFromInstance(service.Name) is { Length: > 0 } refusedBy)
            {
                refused.Add(refusedBy);
            }
        }

        return new AnnouncedConnections(connected, refused);
    }
}

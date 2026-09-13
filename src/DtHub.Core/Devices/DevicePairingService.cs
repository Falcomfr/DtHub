using DtHub.Core.Adb;
using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// Ce que la reprise des appareils annoncés a donné.
/// </summary>
/// <param name="Connected">Adresses désormais connectées.</param>
/// <param name="Refused">
/// Hardware serials of the devices whose address answered and which then
/// refused the connection. Devices whose address stays silent are absent from
/// this list: their announcement may carry another device's address, and
/// silence accuses nobody. A refusal from a live address, on the other hand,
/// says the device no longer recognises this PC's key.
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
    private readonly IAddressProbe? _probe;

    /// <param name="delay">
    /// Attente entre deux sondages mDNS. Injectable pour que les tests
    /// n'attendent pas réellement.
    /// </param>
    /// <param name="probe">
    /// Address probe. Without it the announced address is taken at face value,
    /// which is the behaviour that came before.
    /// </param>
    public DevicePairingService(
        IAdbClient adb,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IAddressProbe? probe = null)
    {
        _adb = adb;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _probe = probe;
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
        if (await ReachableHostAsync(host, pairingPort, cancellationToken).ConfigureAwait(false)
            is not { } reachable)
        {
            return new WirelessPairingResult(
                WirelessPairingStatus.AddressUnreachable,
                Strings.Get("PairingAddressUnreachable"));
        }

        var pairing = await _adb.PairAsync(reachable, pairingPort, pairingCode, cancellationToken)
            .ConfigureAwait(false);

        if (!pairing.Succeeded)
        {
            return new WirelessPairingResult(
                WirelessPairingStatus.PairingFailed,
                AdbErrorInterpreter.Describe(AdbErrorKind.PairingFailed));
        }

        var service = await WaitForConnectServiceAsync(reachable, cancellationToken).ConfigureAwait(false);

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
    /// The address to actually pair against, or <c>null</c> when none answers.
    /// Without a probe, the announced address is handed back untouched.
    /// </summary>
    private async Task<string?> ReachableHostAsync(
        string host,
        int pairingPort,
        CancellationToken cancellationToken)
    {
        if (_probe is null)
        {
            return host;
        }

        if (await _probe.RespondsAsync(host, pairingPort, cancellationToken).ConfigureAwait(false))
        {
            return host;
        }

        // The fallback does not read the announcements again: they are the
        // ones that lie. Only "adb devices" lists live connections, hence
        // verified addresses. When the device being paired is not there yet,
        // which is the case of a brand new phone, asking is all that is left.
        var devices = await _adb.ListDevicesAsync(false, cancellationToken).ConfigureAwait(false);

        foreach (var candidate in devices
            .Select(entry => entry.Host)
            .OfType<string>()
            .Where(known => !string.Equals(known, host, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await _probe.RespondsAsync(candidate, pairingPort, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return null;
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
    /// Polls mDNS until the connect service of the given host shows up, or
    /// until the deadline passes.
    ///
    /// The announcement is read for its port, not for its address. The two do
    /// not always belong together: as soon as two phones announce themselves,
    /// ADB gives a single address to every instance it lists, and one of them
    /// carries the other device's. So the host handed back is the one that
    /// answered, and the announced port is what gets tried on it.
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

            var announced = services.Where(s => s.IsConnect).ToList();

            var match = announced.FirstOrDefault(
                s => string.Equals(s.Host, host, StringComparison.Ordinal));

            if (match is not null)
            {
                return match;
            }

            match = await PortAnsweringOnAsync(host, announced, cancellationToken).ConfigureAwait(false);

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
    /// The first announcement whose port answers on the given host, handed
    /// back at that address rather than the one it carried.
    ///
    /// Nothing without a probe: an address is measured, never guessed.
    /// </summary>
    private async Task<MdnsService?> PortAnsweringOnAsync(
        string host,
        IReadOnlyList<MdnsService> announced,
        CancellationToken cancellationToken)
    {
        if (_probe is null)
        {
            return null;
        }

        foreach (var service in announced)
        {
            if (await _probe.RespondsAsync(host, service.Port, cancellationToken).ConfigureAwait(false))
            {
                return service with { Host = host };
            }
        }

        return null;
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

            // An announcement's address is not proof. This file long claimed
            // the opposite, that a device saying itself which port it listens
            // on could not carry a stale address. That is wrong: as soon as
            // two phones announce themselves, ADB gives a single address to
            // every instance it lists, and one of them carries the other
            // device's. An address that does not answer is therefore nobody's
            // fault, and must be neither connected to nor held against the
            // phone.
            if (_probe is not null
                && !await _probe.RespondsAsync(service.Host, service.Port, cancellationToken)
                    .ConfigureAwait(false))
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

            // An address that answers and still refuses, on the other hand,
            // says something: the phone no longer recognises this PC's key,
            // and only a fresh pairing will give it back.
            if (MdnsDeviceName.HardwareSerialFromInstance(service.Name) is { Length: > 0 } refusedBy)
            {
                refused.Add(refusedBy);
            }
        }

        return new AnnouncedConnections(connected, refused);
    }
}

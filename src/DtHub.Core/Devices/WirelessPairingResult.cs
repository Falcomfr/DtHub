namespace DtHub.Core.Devices;

/// <summary>
/// Outcome of a wireless pairing, from the user's point of view.
/// </summary>
public enum WirelessPairingStatus
{
    /// <summary>Paired and connected: there is nothing left to do.</summary>
    Connected,

    /// <summary>
    /// The phone refused the code, or the pairing did not succeed.
    /// </summary>
    PairingFailed,

    /// <summary>
    /// Pairing succeeded, but the connection port was not
    /// discovered. The network is probably blocking mDNS; the user
    /// can enter the port shown on the phone.
    /// </summary>
    ConnectPortNotFound,

    /// <summary>Pairing succeeded but the connection failed.</summary>
    ConnectFailed,

    /// <summary>
    /// The pairing address does not answer, so the code was not spent. The
    /// mDNS announcement pointed at the wrong device; the user can type the
    /// address shown on the phone.
    /// </summary>
    AddressUnreachable,
}

/// <summary>Full result of a pairing, ready to be displayed.</summary>
public sealed record WirelessPairingResult(
    WirelessPairingStatus Status,
    string UserMessage,
    string? Address = null,
    string? DeviceGuid = null)
{
    /// <summary>
    /// True when the phone accepted the code, whatever happened to
    /// the connection afterwards. Does not therefore mean there is
    /// anything to play: <see cref="Connected"/> is what says that.
    /// </summary>
    public bool Paired => Status
        is not WirelessPairingStatus.PairingFailed
        and not WirelessPairingStatus.AddressUnreachable;

    public bool Connected => Status == WirelessPairingStatus.Connected;

    /// <summary>
    /// True when only the port is missing. The network announced
    /// nothing, but the phone shows that port under "Wireless
    /// debugging", and the pairing is already done: there is
    /// nothing left to redo, only to read it.
    /// </summary>
    public bool NeedsPort => Status == WirelessPairingStatus.ConnectPortNotFound;

    /// <summary>
    /// True when the address itself is what must be supplied. Nothing is
    /// gained in that case, but nothing is lost either: the code was not used,
    /// and the phone's screen shows the address, which does not lie.
    /// </summary>
    public bool NeedsAddress => Status == WirelessPairingStatus.AddressUnreachable;
}

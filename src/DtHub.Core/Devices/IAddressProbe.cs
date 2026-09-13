namespace DtHub.Core.Devices;

/// <summary>
/// Tells whether an address accepts a TCP connection, without sending anything.
///
/// This exists to separate two failures that ADB reports identically.
/// <c>adb pair</c> answers "protocol fault (couldn't read status message)"
/// both when the address is unreachable and when the code is refused, so the
/// text decides nothing and only a probe can. Measured on ADB 37.0.0.
///
/// The implementation lives in <c>Infrastructure</c>: <c>Core</c> does not
/// touch the network, and this interface is what keeps the decision logic pure
/// and replayable without hardware.
/// </summary>
public interface IAddressProbe
{
    /// <summary>
    /// True when something listens on the address. False on refusal, timeout
    /// or unreachable host: the caller does not need to tell those apart, it
    /// only asks whether trying is worth it.
    /// </summary>
    Task<bool> RespondsAsync(string host, int port, CancellationToken cancellationToken = default);
}

using System.Net.Sockets;

using DtHub.Core.Devices;

namespace DtHub.Infrastructure.Devices;

/// <summary>
/// Probes an address by opening a TCP connection and closing it at once.
/// Nothing is sent and nothing is read: the only question asked is whether
/// something listens here.
/// </summary>
public sealed class TcpAddressProbe : IAddressProbe
{
    /// <summary>
    /// How long to wait before calling the address silent.
    ///
    /// Deliberately short: the probe runs while the user watches the window,
    /// and an address outside the network would otherwise let the system wait
    /// about twenty seconds before answering.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<bool> RespondsAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            return false;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);

        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);

            return client.Connected;
        }
        catch (OperationCanceledException)
        {
            // The probe's own deadline is an answer; a cancellation asked for
            // by the caller is not, and must travel back up.
            cancellationToken.ThrowIfCancellationRequested();

            return false;
        }
        catch (SocketException)
        {
            // Refusal, unknown host and unreachable network are the expected
            // answer, not an incident: they are exactly what the probe came to
            // measure, and the false it returns already says so.
            return false;
        }
    }
}

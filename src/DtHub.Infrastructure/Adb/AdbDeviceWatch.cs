using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DtHub.Infrastructure.Adb;

/// <summary>
/// Says when ADB's device list changes, the moment the ADB server knows it.
///
/// **Asked of the server, which tells.** The panel polls every few seconds
/// and skips a tick while a sweep runs, so a phone cut on 2026-10-04 was
/// shown gone 5.7 seconds after ADB had reported it closed. Here the server
/// writes a list on every change, and nothing is asked in between: an idle
/// connection costs nothing.
///
/// **Spoken directly, not through "adb track-devices".** Its output is read
/// line by line by the process runner, and the empty list, the last phone
/// gone, is "0000" without a line break: the one disconnection that matters
/// most would never have been read. The protocol frames each list with its
/// length, and that framing is read here.
/// </summary>
public sealed class AdbDeviceWatch
{
    private const string Request = "host:track-devices";

    /// <summary>The ADB server's port, which nothing in the application changes.</summary>
    public int Port { get; init; } = 5037;

    /// <summary>
    /// Pause before reconnecting, when the server is not running yet or has
    /// just restarted.
    /// </summary>
    public TimeSpan Retry { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Raised on a thread pool thread, once per list announced.</summary>
    public event Action? Changed;

    /// <summary>Watches until cancelled, and returns quietly then.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await WatchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Asked for by the caller: the watch ends here, as documented.
                return;
            }
            catch (Exception exception) when (exception is IOException or SocketException or FormatException)
            {
                // A server not started yet, restarted or killed: the poll still
                // runs, and the next attempt picks the server back up.
            }

            try
            {
                await Task.Delay(Retry, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Same as above, cancelled during the pause.
                return;
            }
        }
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, Port, cancellationToken).ConfigureAwait(false);

        var stream = client.GetStream();
        await stream
            .WriteAsync(Encoding.ASCII.GetBytes($"{Request.Length:x4}{Request}"), cancellationToken)
            .ConfigureAwait(false);

        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (!"OKAY"u8.SequenceEqual(header))
        {
            return;
        }

        while (true)
        {
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

            var length = int.Parse(Encoding.ASCII.GetString(header), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            await stream.ReadExactlyAsync(new byte[length], cancellationToken).ConfigureAwait(false);

            Changed?.Invoke();
        }
    }
}

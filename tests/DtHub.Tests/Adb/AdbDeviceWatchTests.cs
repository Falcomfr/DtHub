using System.Net;
using System.Net.Sockets;
using System.Text;

using DtHub.Infrastructure.Adb;

namespace DtHub.Tests.Adb;

/// <summary>
/// The test plays the ADB server: it accepts the watch's connection, checks
/// the request, and announces device lists the way the server does.
/// </summary>
public class AdbDeviceWatchTests
{
    private static TcpListener Listening(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static byte[] Frame(string body) => Encoding.ASCII.GetBytes($"{body.Length:x4}{body}");

    private static async Task<NetworkStream> AcceptAsync(TcpListener listener, CancellationToken token)
    {
        var client = await listener.AcceptTcpClientAsync(token);
        var stream = client.GetStream();

        var request = new byte[22];
        await stream.ReadExactlyAsync(request, token);
        Assert.Equal("0012host:track-devices", Encoding.ASCII.GetString(request));

        await stream.WriteAsync("OKAY"u8.ToArray(), token);

        return stream;
    }

    [Fact]
    public async Task Chaque_liste_annoncee_est_signalee_y_compris_la_liste_vide()
    {
        var listener = Listening(out var port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var signals = new SemaphoreSlim(0);

        var watch = new AdbDeviceWatch { Port = port };
        watch.Changed += () => signals.Release();
        var running = watch.RunAsync(timeout.Token);

        try
        {
            await using var stream = await AcceptAsync(listener, timeout.Token);

            await stream.WriteAsync(Frame("192.168.1.16:41855\tdevice\n"), timeout.Token);
            Assert.True(await signals.WaitAsync(TimeSpan.FromSeconds(5)));

            // The last phone gone: ADB sends "0000" and no line break. Read
            // line by line, as adb.exe's output would be, this is the
            // disconnection that would go unseen.
            await stream.WriteAsync(Frame(string.Empty), timeout.Token);
            Assert.True(await signals.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await timeout.CancelAsync();
            await running;
            listener.Stop();
        }
    }

    [Fact]
    public async Task Un_serveur_adb_qui_coupe_est_rejoint()
    {
        var listener = Listening(out var port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var signals = new SemaphoreSlim(0);

        var watch = new AdbDeviceWatch { Port = port, Retry = TimeSpan.FromMilliseconds(50) };
        watch.Changed += () => signals.Release();
        var running = watch.RunAsync(timeout.Token);

        try
        {
            // The server restarts: the first connection drops unannounced.
            (await AcceptAsync(listener, timeout.Token)).Close();

            await using var stream = await AcceptAsync(listener, timeout.Token);

            await stream.WriteAsync(Frame(string.Empty), timeout.Token);
            Assert.True(await signals.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await timeout.CancelAsync();
            await running;
            listener.Stop();
        }
    }
}

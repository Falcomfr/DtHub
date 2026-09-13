using System.Net;
using System.Net.Sockets;

using DtHub.Infrastructure.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// Exercises the address probe on the loopback interface. No network is
/// required and no phone either: the listener is opened by the test itself,
/// on a port the system hands out.
/// </summary>
public class TcpAddressProbeTests
{
    private static TcpAddressProbe Probe() =>
        new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>Opens a listener and hands back its port.</summary>
    private static TcpListener Listening(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;

        return listener;
    }

    [Fact]
    public async Task Une_adresse_qui_ecoute_repond()
    {
        var listener = Listening(out var port);

        try
        {
            Assert.True(await Probe().RespondsAsync("127.0.0.1", port, CancellationToken.None));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Un_port_ferme_ne_repond_pas()
    {
        // The port is reserved then released: nothing listens on it, which is
        // exactly the stale mDNS announcement we are trying to recognise.
        var listener = Listening(out var port);
        listener.Stop();

        Assert.False(await Probe().RespondsAsync("127.0.0.1", port, CancellationToken.None));
    }

    [Fact]
    public async Task Une_adresse_vide_ne_repond_pas()
    {
        // The user types the address themselves in the degraded case: the
        // probe must answer false, not throw.
        Assert.False(await Probe().RespondsAsync("   ", 43415, CancellationToken.None));
    }
}

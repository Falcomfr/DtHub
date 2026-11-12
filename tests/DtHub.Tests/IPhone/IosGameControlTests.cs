using System.Net;
using System.Net.Sockets;
using System.Text;

using DtHub.Infrastructure.IPhone;

namespace DtHub.Tests.IPhone;

/// <summary>
/// The requests DT Hub sends to the control server inside the iPhone tool.
/// </summary>
public sealed class IosGameControlTests
{
    [Fact]
    public async Task Une_commande_envoie_la_longueur_de_son_corps()
    {
        // The tool reads a body by its Content-Length: sent in chunks, without
        // it, the body arrived empty and every launch and stop answered
        // KeyError('bundle') on a player's iPad and iPhone (2026-11-12).
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var received = ReceiveOneAsync(listener);
        await new IosGameControl(port).LaunchAsync("com.ankama.dofustouch", CancellationToken.None);
        var request = await received;
        listener.Stop();

        Assert.Contains("Content-Length:", request, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Transfer-Encoding: chunked", request, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("{\"bundle\":\"com.ankama.dofustouch\"}", request, StringComparison.Ordinal);
    }

    private static async Task<string> ReceiveOneAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var text = new StringBuilder();
        var buffer = new byte[4096];

        // Headers, then the body whatever its framing: a chunked body ends with "0\r\n\r\n".
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var all = text.ToString();
            if (all.Contains("\r\n\r\n", StringComparison.Ordinal)
                && (all.EndsWith('}') || all.EndsWith("0\r\n\r\n", StringComparison.Ordinal)))
            {
                break;
            }
        }

        var reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
        await stream.WriteAsync(reply);
        return text.ToString();
    }
}

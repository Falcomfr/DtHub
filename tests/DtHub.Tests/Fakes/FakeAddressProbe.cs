using DtHub.Core.Devices;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated address probe. Only declared addresses answer and everything else
/// is silent, which is the interesting case: an mDNS announcement pointing at a
/// machine that does not listen on that port.
/// </summary>
public sealed class FakeAddressProbe : IAddressProbe
{
    /// <summary>Addresses that accept a connection, as <c>host:port</c>.</summary>
    public HashSet<string> Responding { get; } = new(StringComparer.Ordinal);

    /// <summary>Probed addresses, in order, to check what was attempted.</summary>
    public List<string> Probed { get; } = [];

    public FakeAddressProbe Answering(string address)
    {
        Responding.Add(address);

        return this;
    }

    public Task<bool> RespondsAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var address = $"{host}:{port}";

        Probed.Add(address);

        return Task.FromResult(Responding.Contains(address));
    }
}

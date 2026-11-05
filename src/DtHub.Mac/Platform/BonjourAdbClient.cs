using System.Text;

using DtHub.Core.Adb;
using DtHub.Core.Processes;
using DtHub.Infrastructure.Adb;

namespace DtHub.Mac.Platform;

/// <summary>
/// The PC's adb client, but for the wireless debugging announcements,
/// read from macOS's own Bonjour (Native/MacBonjour.swift). adb's
/// discovery finds nothing on a Mac, whatever its backend: a phone whose
/// port changed after a restart could not be found again and showed
/// "pair again", and the pairing window listed no phone nearby. What adb
/// still finds is kept, so a future adb that works on the Mac loses
/// nothing.
/// </summary>
public sealed class BonjourAdbClient(AdbClient adb) : IAdbClient
{
    private readonly AdbClient _adb = adb;

    public async Task<IReadOnlyList<MdnsService>> ListMdnsServicesAsync(CancellationToken cancellationToken = default)
    {
        var fromAdb = await _adb.ListMdnsServicesAsync(cancellationToken).ConfigureAwait(false);

        // A second or two of waiting on the network: off the caller's thread.
        var fromMac = await Task.Run(Browse, cancellationToken).ConfigureAwait(false);

        return [.. fromAdb.Concat(fromMac).DistinctBy(s => (s.Name, s.ServiceType))];
    }

    private static IReadOnlyList<MdnsService> Browse()
    {
        var buffer = new byte[64 * 1024];

        if (MacNative.dthub_mdns_services(buffer, buffer.Length) < 0)
        {
            return [];
        }

        var end = Array.IndexOf(buffer, (byte)0);

        return AdbOutputParser.ParseMdnsServices(Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end));
    }

    public Task StartServerAsync(CancellationToken cancellationToken = default) =>
        _adb.StartServerAsync(cancellationToken);

    public Task StopServerAsync(CancellationToken cancellationToken = default) =>
        _adb.StopServerAsync(cancellationToken);

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        _adb.GetVersionAsync(cancellationToken);

    public Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(bool detailed = true, CancellationToken cancellationToken = default) =>
        _adb.ListDevicesAsync(detailed, cancellationToken);

    public Task<ProcessResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        IReadOnlyCollection<string>? sensitiveValues = null,
        CancellationToken cancellationToken = default) =>
        _adb.ExecuteAsync(serial, arguments, timeout, sensitiveValues, cancellationToken);

    public Task<string> ShellAsync(
        string serial, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _adb.ShellAsync(serial, arguments, timeout, cancellationToken);

    public Task<ProcessBytes> ExecOutAsync(
        string serial, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _adb.ExecOutAsync(serial, arguments, timeout, cancellationToken);

    public Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(string serial, CancellationToken cancellationToken = default) =>
        _adb.GetPropertiesAsync(serial, cancellationToken);

    public Task<AdbPairResult> PairAsync(string host, int pairingPort, string pairingCode, CancellationToken cancellationToken = default) =>
        _adb.PairAsync(host, pairingPort, pairingCode, cancellationToken);

    public Task<AdbConnectResult> ConnectAsync(string host, int port, CancellationToken cancellationToken = default) =>
        _adb.ConnectAsync(host, port, cancellationToken);

    public Task DisconnectAsync(string? address = null, CancellationToken cancellationToken = default) =>
        _adb.DisconnectAsync(address, cancellationToken);

    public Task<bool> WaitForDeviceAsync(string serial, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _adb.WaitForDeviceAsync(serial, timeout, cancellationToken);
}

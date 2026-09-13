using System.Globalization;

namespace DtHub.Core.Adb;

/// <summary>
/// Reading ADB's text outputs. Pure functions, with no state and no
/// input or output: this is what makes most of the behavior testable
/// without a phone. No unexpected line should ever cause an
/// exception.
/// </summary>
public static class AdbOutputParser
{
    private const string DeviceListHeader = "List of devices attached";

    /// <summary>
    /// Reads the output of <c>adb devices</c> the same way as
    /// <c>adb devices -l</c>. Daemon startup lines and empty lines
    /// are ignored.
    /// </summary>
    public static IReadOnlyList<AdbDeviceEntry> ParseDevices(string? output)
    {
        var entries = new List<AdbDeviceEntry>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return entries;
        }

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();

            // Common noise: "* daemon not running; starting now at
            // tcp:5037".
            if (line.Length == 0 || line.StartsWith('*') || line.StartsWith(DeviceListHeader, StringComparison.Ordinal))
            {
                continue;
            }

            var entry = ParseDeviceLine(line);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// Reads a single line. The format is "serial &lt;spaces&gt;
    /// state" followed, in detailed mode, by a series of
    /// <c>key:value</c> pairs.
    /// </summary>
    public static AdbDeviceEntry? ParseDeviceLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return null;
        }

        var serial = tokens[0];
        var rawState = tokens[1];

        string? product = null, model = null, device = null, transportId = null, usbPath = null;

        foreach (var token in tokens.Skip(2))
        {
            var separator = token.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == token.Length - 1)
            {
                continue;
            }

            var key = token[..separator];
            var value = token[(separator + 1)..];

            switch (key)
            {
                case "product": product = value; break;
                case "model": model = value; break;
                case "device": device = value; break;
                case "transport_id": transportId = value; break;
                case "usb": usbPath = value; break;
                default: break;
            }
        }

        var (host, port) = SplitNetworkSerial(serial);

        return new AdbDeviceEntry
        {
            Serial = serial,
            State = ParseState(rawState),
            RawState = rawState,
            ConnectionKind = DetectConnectionKind(serial, usbPath),
            Product = product,
            Model = model,
            Device = device,
            TransportId = transportId,
            UsbPath = usbPath,
            Host = host,
            Port = port,
        };
    }

    /// <summary>
    /// Translates ADB's state label. Any unknown label gives
    /// Unknown.
    /// </summary>
    public static AdbDeviceState ParseState(string? rawState) => rawState switch
    {
        "device" => AdbDeviceState.Device,
        "offline" => AdbDeviceState.Offline,
        "unauthorized" => AdbDeviceState.Unauthorized,
        "authorizing" => AdbDeviceState.Authorizing,
        "connecting" => AdbDeviceState.Connecting,
        "no permissions" or "no" => AdbDeviceState.NoPermissions,
        "bootloader" => AdbDeviceState.Bootloader,
        "recovery" => AdbDeviceState.Recovery,
        "sideload" => AdbDeviceState.Sideload,
        "rescue" => AdbDeviceState.Rescue,
        "host" => AdbDeviceState.Host,
        "detached" => AdbDeviceState.Detached,
        _ => AdbDeviceState.Unknown,
    };

    /// <summary>
    /// Infers the connection type. The USB path reported in detailed
    /// mode settles it; otherwise a serial number of the form
    /// <c>host:port</c> denotes a wireless connection.
    /// </summary>
    public static AdbConnectionKind DetectConnectionKind(string? serial, string? usbPath = null)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return AdbConnectionKind.Unknown;
        }

        if (serial.StartsWith("emulator-", StringComparison.Ordinal))
        {
            return AdbConnectionKind.Emulator;
        }

        if (!string.IsNullOrEmpty(usbPath))
        {
            return AdbConnectionKind.Usb;
        }

        // mDNS service name, for example
        // adb-XXXX-YYYY._adb-tls-connect._tcp.
        if (serial.StartsWith("adb-", StringComparison.Ordinal) && serial.Contains("._", StringComparison.Ordinal))
        {
            return AdbConnectionKind.Wireless;
        }

        return SplitNetworkSerial(serial).Host is not null
            ? AdbConnectionKind.Wireless
            : AdbConnectionKind.Usb;
    }

    /// <summary>
    /// Splits a network serial number into host and port. Handles
    /// the bracketed IPv6 form. Returns two null values if it is not
    /// one.
    /// </summary>
    public static (string? Host, int? Port) SplitNetworkSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return (null, null);
        }

        var separator = serial.LastIndexOf(':');
        if (separator <= 0 || separator == serial.Length - 1)
        {
            return (null, null);
        }

        var host = serial[..separator];
        var portText = serial[(separator + 1)..];

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            return (null, null);
        }

        // A USB serial number contains no dot or bracket; this check
        // avoids mistaking "ABC:1234" for an address.
        var looksLikeHost = host.Contains('.', StringComparison.Ordinal)
            || host.StartsWith('[')
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

        return looksLikeHost ? (host.Trim('[', ']'), port) : (null, null);
    }

    /// <summary>
    /// Reads the output of <c>getprop</c>, in the format
    /// <c>[key]: [value]</c>. A value can itself contain brackets,
    /// hence searching for the last closing bracket rather than a
    /// naive split.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseGetProp(string? output)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(output))
        {
            return properties;
        }

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            if (line.Length < 6 || !line.StartsWith('[') || !line.EndsWith(']'))
            {
                continue;
            }

            var separator = line.IndexOf("]: [", StringComparison.Ordinal);
            if (separator <= 1)
            {
                continue;
            }

            var key = line[1..separator];
            var value = line[(separator + 4)..^1];

            if (key.Length > 0)
            {
                properties[key] = value;
            }
        }

        return properties;
    }


    /// <summary>
    /// Reads the output of <c>adb mdns services</c>. Columns are
    /// separated by tabs or by spaces depending on the ADB version,
    /// hence a split on any whitespace.
    /// </summary>
    public static IReadOnlyList<MdnsService> ParseMdnsServices(string? output)
    {
        var services = new List<MdnsService>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return services;
        }

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();

            if (line.Length == 0
                || line.StartsWith('*')
                || line.StartsWith("List of discovered mdns services", StringComparison.Ordinal)
                || line.StartsWith("mdns daemon version", StringComparison.Ordinal))
            {
                continue;
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3)
            {
                continue;
            }

            var (host, port) = SplitNetworkSerial(tokens[2]);
            if (host is null || port is null)
            {
                continue;
            }

            services.Add(new MdnsService(tokens[0], tokens[1], host, port.Value));
        }

        return services;
    }

    /// <summary>
    /// Reads the output of <c>adb pair</c>. The pairing code never
    /// appears in the output, and must never be copied into a log.
    /// </summary>
    public static AdbPairResult ParsePairResult(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return AdbPairResult.Failure(null);
        }

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("Successfully paired", StringComparison.OrdinalIgnoreCase))
            {
                return AdbPairResult.Success(ExtractGuid(line));
            }

            if (line.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("adb: error", StringComparison.OrdinalIgnoreCase))
            {
                return AdbPairResult.Failure(line);
            }
        }

        return AdbPairResult.Failure(output.Trim());
    }

    /// <summary>Reads the output of <c>adb connect</c>.</summary>
    public static AdbConnectResult ParseConnectResult(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return AdbConnectResult.Failure(null);
        }

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("already connected", StringComparison.OrdinalIgnoreCase))
            {
                return AdbConnectResult.Already;
            }

            if (line.StartsWith("connected to", StringComparison.OrdinalIgnoreCase))
            {
                return AdbConnectResult.Connected;
            }

            if (line.StartsWith("failed to connect", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("cannot connect", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("unable to connect", StringComparison.OrdinalIgnoreCase))
            {
                return AdbConnectResult.Failure(line);
            }
        }

        return AdbConnectResult.Failure(output.Trim());
    }

    /// <summary>
    /// Extracts <c>guid=...</c> from a successful pairing line.
    /// </summary>
    private static string? ExtractGuid(string line)
    {
        const string marker = "guid=";

        var start = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = line.IndexOfAny([']', ' '], start);

        return end < 0 ? line[start..] : line[start..end];
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r'));
}

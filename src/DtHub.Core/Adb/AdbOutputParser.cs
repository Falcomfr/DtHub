using System.Globalization;

namespace DtHub.Core.Adb;

/// <summary>
/// Lecture des sorties texte d'ADB. Fonctions pures, sans état ni entrée
/// sortie : c'est ce qui rend l'essentiel du comportement testable sans
/// téléphone. Aucune ligne inattendue ne doit provoquer d'exception.
/// </summary>
public static class AdbOutputParser
{
    private const string DeviceListHeader = "List of devices attached";

    /// <summary>
    /// Lit la sortie de <c>adb devices</c> comme celle de <c>adb devices -l</c>.
    /// Les lignes de démarrage du démon et les lignes vides sont ignorées.
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

            // Bruit courant : « * daemon not running; starting now at tcp:5037 ».
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
    /// Lit une ligne unique. Le format est « série &lt;espaces&gt; état » suivi,
    /// en mode détaillé, d'une suite de paires <c>clé:valeur</c>.
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

    /// <summary>Traduit le libellé d'état d'ADB. Tout libellé inconnu donne Unknown.</summary>
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
    /// Déduit le type de connexion. Le chemin USB rapporté en mode détaillé
    /// tranche ; sinon un numéro de série de la forme <c>hôte:port</c> désigne
    /// une connexion sans fil.
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

        // Nom de service mDNS, par exemple adb-XXXX-YYYY._adb-tls-connect._tcp.
        if (serial.StartsWith("adb-", StringComparison.Ordinal) && serial.Contains("._", StringComparison.Ordinal))
        {
            return AdbConnectionKind.Wireless;
        }

        return SplitNetworkSerial(serial).Host is not null
            ? AdbConnectionKind.Wireless
            : AdbConnectionKind.Usb;
    }

    /// <summary>
    /// Sépare un numéro de série réseau en hôte et port. Gère la forme IPv6
    /// entre crochets. Retourne deux valeurs nulles si ce n'en est pas un.
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

        // Un numéro de série USB ne contient pas de point ni de crochet ; cette
        // vérification évite de prendre « ABC:1234 » pour une adresse.
        var looksLikeHost = host.Contains('.', StringComparison.Ordinal)
            || host.StartsWith('[')
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

        return looksLikeHost ? (host.Trim('[', ']'), port) : (null, null);
    }

    /// <summary>
    /// Lit la sortie de <c>getprop</c>, au format <c>[clé]: [valeur]</c>.
    /// Une valeur peut elle-même contenir des crochets, d'où la recherche du
    /// dernier crochet fermant plutôt qu'un découpage naïf.
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
    /// Lit la sortie de <c>adb mdns services</c>. Les colonnes sont séparées
    /// par des tabulations ou par des espaces selon les versions d'ADB, d'où
    /// un découpage sur tout blanc.
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
    /// Lit la sortie de <c>adb pair</c>. Le code d'appairage n'apparaît jamais
    /// dans la sortie, et ne doit jamais être recopié dans un journal.
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

    /// <summary>Lit la sortie de <c>adb connect</c>.</summary>
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

    /// <summary>Extrait <c>guid=...</c> d'une ligne d'appairage réussi.</summary>
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

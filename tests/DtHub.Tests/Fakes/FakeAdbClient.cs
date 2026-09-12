using DtHub.Core.Adb;
using DtHub.Core.Processes;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Client ADB simulé, piloté par des sorties textuelles. On reste ainsi au
/// plus près de ce que rend le vrai ADB, parseurs compris.
/// </summary>
public sealed class FakeAdbClient : IAdbClient
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly HashSet<string> _propertyFailures = new(StringComparer.Ordinal);

    /// <summary>Sortie renvoyée par <c>adb devices -l</c>.</summary>
    public string DevicesOutput { get; set; } = "List of devices attached\n";

    /// <summary>Erreur levée par la liste des appareils, si elle est renseignée.</summary>
    public AdbException? DevicesError { get; set; }

    /// <summary>Nombre d'appels à <c>getprop</c>, pour vérifier la mise en cache.</summary>
    public int GetPropertiesCallCount { get; private set; }

    public int ListDevicesCallCount { get; private set; }

    /// <summary>Déclare la sortie de <c>getprop</c> pour un numéro de série.</summary>
    public FakeAdbClient WithProperties(string serial, string getPropOutput)
    {
        _properties[serial] = getPropOutput;
        return this;
    }

    /// <summary>Fait échouer <c>getprop</c> pour un numéro de série précis.</summary>
    public FakeAdbClient FailProperties(string serial)
    {
        _propertyFailures.Add(serial);
        return this;
    }

    public Task StartServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("Android Debug Bridge version 1.0.41");

    public Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(
        bool detailed = true,
        CancellationToken cancellationToken = default)
    {
        ListDevicesCallCount++;

        return DevicesError is not null
            ? Task.FromException<IReadOnlyList<AdbDeviceEntry>>(DevicesError)
            : Task.FromResult(AdbOutputParser.ParseDevices(DevicesOutput));
    }

    private readonly List<(string Match, ProcessResult Result)> _executeRules = [];

    /// <summary>Commandes reçues par <c>ExecuteAsync</c>, jointes par des espaces.</summary>
    public List<string> ExecuteCalls { get; } = [];

    /// <summary>Déclare un résultat pour une commande contenant le motif donné.</summary>
    public FakeAdbClient WithExecute(string argumentsContain, ProcessResult result)
    {
        _executeRules.Add((argumentsContain, result));

        return this;
    }

    public Task<ProcessResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        IReadOnlyCollection<string>? sensitiveValues = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var line = string.Join(' ', arguments);
        ExecuteCalls.Add(line);

        foreach (var (match, result) in _executeRules)
        {
            if (line.Contains(match, StringComparison.Ordinal))
            {
                return Task.FromResult(result);
            }
        }

        return Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = string.Empty,
            StandardError = string.Empty,
            Duration = TimeSpan.Zero,
        });
    }

    private readonly List<(string Match, Func<string> Respond)> _shellRules = [];

    /// <summary>Commandes shell reçues, jointes par des espaces.</summary>
    public List<string> ShellCalls { get; } = [];

    /// <summary>Déclare une sortie shell pour une commande contenant le motif donné.</summary>
    public FakeAdbClient WithShell(string argumentsContain, string output)
    {
        _shellRules.Add((argumentsContain, () => output));
        return this;
    }

    /// <summary>
    /// Déclare une suite de sorties pour une même commande, la dernière valant
    /// pour tous les appels suivants.
    ///
    /// Le téléphone change d'état sous nos pieds : après une installation, le
    /// paquet est là où il n'était pas. Une sortie fixe ne sait pas dire cela,
    /// et une épreuve qui installe puis vérifie échouait donc toujours.
    /// </summary>
    public FakeAdbClient WithShellChanging(string argumentsContain, params string[] outputs)
    {
        var rang = 0;

        _shellRules.Add((argumentsContain, () => outputs[Math.Min(rang++, outputs.Length - 1)]));

        return this;
    }

    /// <summary>Fait échouer une commande shell contenant le motif donné.</summary>
    public FakeAdbClient FailShell(string argumentsContain, AdbErrorKind kind = AdbErrorKind.DeviceOffline)
    {
        _shellRules.Add((argumentsContain, () => throw new AdbException(kind, AdbErrorInterpreter.Describe(kind))));
        return this;
    }

    private readonly List<(string Match, byte[] Output)> _execOutRules = [];

    /// <summary>Commandes « exec-out » reçues, jointes par des espaces.</summary>
    public List<string> ExecOutCalls { get; } = [];

    /// <summary>Déclare une sortie binaire pour une commande contenant le motif donné.</summary>
    public FakeAdbClient WithExecOut(string argumentsContain, byte[] output)
    {
        _execOutRules.Add((argumentsContain, output));

        return this;
    }

    public Task<ProcessBytes> ExecOutAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var joined = string.Join(' ', arguments);
        ExecOutCalls.Add(joined);

        var rule = _execOutRules.FirstOrDefault(
            r => joined.Contains(r.Match, StringComparison.Ordinal));

        return Task.FromResult(rule.Output is null
            ? new ProcessBytes
            {
                ExitCode = 1,
                StandardOutput = [],
                StandardError = $"FakeAdbClient : aucune règle exec-out pour « {joined} »",
            }
            : new ProcessBytes
            {
                ExitCode = 0,
                StandardOutput = rule.Output,
                StandardError = string.Empty,
            });
    }

    public Task<string> ShellAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var joined = string.Join(' ', arguments);
        ShellCalls.Add(joined);

        var rule = _shellRules.FirstOrDefault(r => joined.Contains(r.Match, StringComparison.Ordinal));

        try
        {
            return Task.FromResult(rule.Respond?.Invoke() ?? string.Empty);
        }
        catch (AdbException exception)
        {
            return Task.FromException<string>(exception);
        }
    }

    public Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        GetPropertiesCallCount++;

        if (_propertyFailures.Contains(serial))
        {
            return Task.FromException<IReadOnlyDictionary<string, string>>(
                new AdbException(
                    AdbErrorKind.DeviceUnauthorized,
                    AdbErrorInterpreter.Describe(AdbErrorKind.DeviceUnauthorized)));
        }

        return Task.FromResult(_properties.TryGetValue(serial, out var output)
            ? AdbOutputParser.ParseGetProp(output)
            : (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>Sorties de <c>adb mdns services</c> renvoyées successivement.</summary>
    public Queue<string> MdnsOutputs { get; } = new();

    /// <summary>Résultat renvoyé par l'appairage.</summary>
    public AdbPairResult PairOutcome { get; set; } = AdbPairResult.Success("adb-MATERIEL123-nJyLWZ");

    /// <summary>Adresses pour lesquelles <c>adb connect</c> réussit.</summary>
    public HashSet<string> ConnectableAddresses { get; } = new(StringComparer.Ordinal);

    /// <summary>Adresses effectivement demandées à <c>adb connect</c>, dans l'ordre.</summary>
    public List<string> ConnectAttempts { get; } = [];

    /// <summary>Codes d'appairage vus, pour vérifier qu'ils ne fuitent pas.</summary>
    public List<string> PairingCodesSeen { get; } = [];

    public Task<AdbPairResult> PairAsync(
        string host,
        int pairingPort,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        PairingCodesSeen.Add(pairingCode);
        return Task.FromResult(PairOutcome);
    }

    /// <summary>
    /// Adresses qui ne répondent jamais, pour éprouver l'échéance. Un vrai
    /// téléphone éteint laisse le système attendre vingt-deux secondes.
    /// </summary>
    public HashSet<string> SilentAddresses { get; } = new(StringComparer.Ordinal);

    /// <summary>Le message d'échec rendu, quand on veut en éprouver la lecture.</summary>
    public string? ConnectFailure { get; init; }

    public async Task<AdbConnectResult> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var address = $"{host}:{port}";
        ConnectAttempts.Add(address);

        if (SilentAddresses.Contains(address))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        return ConnectableAddresses.Contains(address)
            ? AdbConnectResult.Connected
            : AdbConnectResult.Failure(ConnectFailure ?? $"cannot connect to {address}: (10061)");
    }

    /// <summary>Adresses déconnectées, dans l'ordre.</summary>
    public List<string> Disconnected { get; } = [];

    public Task DisconnectAsync(string? address = null, CancellationToken cancellationToken = default)
    {
        if (address is { Length: > 0 })
        {
            Disconnected.Add(address);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MdnsService>> ListMdnsServicesAsync(CancellationToken cancellationToken = default)
    {
        var output = MdnsOutputs.Count > 0 ? MdnsOutputs.Dequeue() : string.Empty;
        return Task.FromResult(AdbOutputParser.ParseMdnsServices(output));
    }

    public Task<bool> WaitForDeviceAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

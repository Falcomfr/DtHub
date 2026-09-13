using DtHub.Core.Adb;
using DtHub.Core.Processes;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated ADB client, driven by textual outputs. This keeps things as
/// close as possible to what the real ADB renders, parsers included.
/// </summary>
public sealed class FakeAdbClient : IAdbClient
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly HashSet<string> _propertyFailures = new(StringComparer.Ordinal);

    /// <summary>Output returned by <c>adb devices -l</c>.</summary>
    public string DevicesOutput { get; set; } = "List of devices attached\n";

    /// <summary>
    /// Error thrown by the device list, when one has been set.
    /// </summary>
    public AdbException? DevicesError { get; set; }

    /// <summary>
    /// Number of calls to <c>getprop</c>, to verify caching.
    /// </summary>
    public int GetPropertiesCallCount { get; private set; }

    public int ListDevicesCallCount { get; private set; }

    /// <summary>
    /// Declares the <c>getprop</c> output for a serial number.
    /// </summary>
    public FakeAdbClient WithProperties(string serial, string getPropOutput)
    {
        _properties[serial] = getPropOutput;
        return this;
    }

    /// <summary>
    /// Makes <c>getprop</c> fail for a specific serial number.
    /// </summary>
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

    /// <summary>
    /// Commands received by <c>ExecuteAsync</c>, joined with spaces.
    /// </summary>
    public List<string> ExecuteCalls { get; } = [];

    /// <summary>
    /// Declares a result for a command containing the given pattern.
    /// </summary>
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

    /// <summary>Shell commands received, joined with spaces.</summary>
    public List<string> ShellCalls { get; } = [];

    /// <summary>
    /// Declares a shell output for a command containing the given
    /// pattern.
    /// </summary>
    public FakeAdbClient WithShell(string argumentsContain, string output)
    {
        _shellRules.Add((argumentsContain, () => output));
        return this;
    }

    /// <summary>
    /// Declares a sequence of outputs for the same command, the last one
    /// holding for every following call.
    ///
    /// The phone changes state under our feet: after an install, the
    /// package is where it was not before. A fixed output cannot say
    /// that, so a test that installs and then checks would always fail.
    /// </summary>
    public FakeAdbClient WithShellChanging(string argumentsContain, params string[] outputs)
    {
        var rang = 0;

        _shellRules.Add((argumentsContain, () => outputs[Math.Min(rang++, outputs.Length - 1)]));

        return this;
    }

    /// <summary>
    /// Makes a shell command containing the given pattern fail.
    /// </summary>
    public FakeAdbClient FailShell(string argumentsContain, AdbErrorKind kind = AdbErrorKind.DeviceOffline)
    {
        _shellRules.Add((argumentsContain, () => throw new AdbException(kind, AdbErrorInterpreter.Describe(kind))));
        return this;
    }

    private readonly List<(string Match, byte[] Output)> _execOutRules = [];

    /// <summary>"exec-out" commands received, joined with spaces.</summary>
    public List<string> ExecOutCalls { get; } = [];

    /// <summary>
    /// Declares a binary output for a command containing the given
    /// pattern.
    /// </summary>
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

    /// <summary>
    /// Outputs of <c>adb mdns services</c>, returned one after another.
    /// </summary>
    public Queue<string> MdnsOutputs { get; } = new();

    /// <summary>Result returned by pairing.</summary>
    public AdbPairResult PairOutcome { get; set; } = AdbPairResult.Success("adb-MATERIEL123-nJyLWZ");

    /// <summary>Addresses for which <c>adb connect</c> succeeds.</summary>
    public HashSet<string> ConnectableAddresses { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Addresses actually requested from <c>adb connect</c>, in order.
    /// </summary>
    public List<string> ConnectAttempts { get; } = [];

    /// <summary>
    /// Pairing codes seen, to verify that they do not leak.
    /// </summary>
    public List<string> PairingCodesSeen { get; } = [];

    /// <summary>Addresses actually submitted to pairing, in order.</summary>
    public List<string> PairAttempts { get; } = [];

    public Task<AdbPairResult> PairAsync(
        string host,
        int pairingPort,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        PairAttempts.Add($"{host}:{pairingPort}");
        PairingCodesSeen.Add(pairingCode);
        return Task.FromResult(PairOutcome);
    }

    /// <summary>
    /// Addresses that never answer, to exercise the deadline. A real
    /// phone that is switched off leaves the system waiting for
    /// twenty-two seconds.
    /// </summary>
    public HashSet<string> SilentAddresses { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The failure message returned, when its reading needs to be
    /// exercised.
    /// </summary>
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

    /// <summary>Disconnected addresses, in order.</summary>
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

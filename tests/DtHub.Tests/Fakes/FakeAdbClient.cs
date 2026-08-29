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

    public Task<ProcessResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = string.Empty,
            StandardError = string.Empty,
            Duration = TimeSpan.Zero,
        });

    public Task<string> ShellAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);

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

    public Task<bool> WaitForDeviceAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

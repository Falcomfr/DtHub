using DtHub.Core.Adb;
using DtHub.Core.Processes;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Base pour un faux ADB dont la réponse dépend de la commande, utile lorsque
/// les règles par motif ne suffisent pas à exprimer un enchaînement.
/// </summary>
public abstract class FakeAdbClientBase : IAdbClient
{
    /// <summary>Commandes shell reçues, jointes par des espaces.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Réponse à une commande shell.</summary>
    public abstract string Shell(string joined);

    public Task StartServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("Android Debug Bridge version 1.0.41");

    public Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(
        bool detailed = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AdbDeviceEntry>>([]);

    public Task<ProcessResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        IReadOnlyCollection<string>? sensitiveValues = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = string.Empty,
            StandardError = string.Empty,
            Duration = TimeSpan.Zero,
        });

    /// <summary>
    /// Aucune sortie binaire par défaut : les suites qui héritent de cette base
    /// éprouvent des enchaînements de commandes textuelles.
    /// </summary>
    public virtual Task<ProcessBytes> ExecOutAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProcessBytes
        {
            ExitCode = 1,
            StandardOutput = [],
            StandardError = "FakeAdbClientBase : exec-out non simulé.",
        });

    public Task<string> ShellAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var joined = string.Join(' ', arguments);
        Calls.Add(joined);

        return Task.FromResult(Shell(joined));
    }

    public Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal));

    public Task<AdbPairResult> PairAsync(
        string host,
        int pairingPort,
        string pairingCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdbPairResult.Failure("non implémenté"));

    public Task<AdbConnectResult> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdbConnectResult.Failure("non implémenté"));

    public Task DisconnectAsync(string? address = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<MdnsService>> ListMdnsServicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MdnsService>>([]);

    public Task<bool> WaitForDeviceAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

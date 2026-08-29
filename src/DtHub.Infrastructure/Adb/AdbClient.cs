using DtHub.Core.Adb;
using DtHub.Core.Processes;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Adb;

/// <summary>
/// Implémentation d'<see cref="IAdbClient"/> au-dessus de l'exécutable ADB.
/// Aucune console n'apparaît, chaque appel est borné dans le temps et
/// annulable, et les échecs sont traduits avant de remonter à l'interface.
/// </summary>
public sealed partial class AdbClient : IAdbClient
{
    /// <summary>
    /// Marge confortable : le premier appel démarre le serveur ADB, ce qui
    /// prend plus de temps que les suivants.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(45);

    private readonly IProcessRunner _runner;
    private readonly IAdbLocator _locator;
    private readonly ILogger<AdbClient> _logger;

    public AdbClient(IProcessRunner runner, IAdbLocator locator, ILogger<AdbClient> logger)
    {
        _runner = runner;
        _locator = locator;
        _logger = logger;
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(null, ["start-server"], ServerTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw Translate(result, null, "Démarrage du serveur ADB");
        }

        LogServerStarted();
    }

    public async Task StopServerAsync(CancellationToken cancellationToken = default)
    {
        // Le serveur est partagé avec les autres outils installés sur la
        // machine. L'appelant a la responsabilité de ne demander cet arrêt que
        // sur action explicite de l'utilisateur.
        LogServerStopRequested();

        await ExecuteAsync(null, ["kill-server"], ServerTimeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(null, ["version"], DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw Translate(result, null, "Lecture de la version d'ADB");
        }

        // Première ligne : « Android Debug Bridge version 1.0.41 ».
        var firstLine = result.StandardOutput
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);

        return firstLine ?? "inconnue";
    }

    public async Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(
        bool detailed = true,
        CancellationToken cancellationToken = default)
    {
        string[] arguments = detailed ? ["devices", "-l"] : ["devices"];

        var result = await ExecuteAsync(null, arguments, DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw Translate(result, null, "Liste des appareils");
        }

        return AdbOutputParser.ParseDevices(result.StandardOutput);
    }

    public async Task<ProcessResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var adbPath = await _locator.GetAdbPathAsync(cancellationToken).ConfigureAwait(false);

        List<string> fullArguments = [];
        if (!string.IsNullOrWhiteSpace(serial))
        {
            fullArguments.Add("-s");
            fullArguments.Add(serial);
        }

        fullArguments.AddRange(arguments);

        var request = new ProcessRequest
        {
            FileName = adbPath,
            Arguments = fullArguments,
            Timeout = timeout ?? DefaultTimeout,
        };

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogCommand(request.ToDisplayString());
        }

        try
        {
            var result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded && _logger.IsEnabled(LogLevel.Debug))
            {
                LogCommandFailed(result.ExitCode, result.TimedOut, Truncate(result.OutputOrError));
            }

            return result;
        }
        catch (ProcessLaunchException exception)
        {
            throw new AdbException(
                AdbErrorKind.AdbUnavailable,
                AdbErrorInterpreter.Describe(AdbErrorKind.AdbUnavailable),
                exception.Message,
                exception);
        }
    }

    public async Task<string> ShellAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(arguments);

        var result = await ExecuteAsync(serial, ["shell", .. arguments], timeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw Translate(result, serial, $"shell {string.Join(' ', arguments)}");
        }

        // Le shell Android rend un code 0 même pour certaines erreurs : on
        // inspecte donc aussi la sortie avant de la considérer comme valide.
        if (AdbErrorInterpreter.Classify(result.OutputOrError) is { } kind)
        {
            throw new AdbException(
                kind,
                AdbErrorInterpreter.Describe(kind),
                Truncate(result.OutputOrError));
        }

        return result.StandardOutput;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        var output = await ShellAsync(serial, ["getprop"], DefaultTimeout, cancellationToken)
            .ConfigureAwait(false);

        return AdbOutputParser.ParseGetProp(output);
    }

    public async Task<bool> WaitForDeviceAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var result = await ExecuteAsync(serial, ["wait-for-device"], timeout, cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded;
    }

    /// <summary>
    /// Transforme un résultat en échec en exception porteuse d'un message
    /// affichable. La sortie brute reste confinée aux détails techniques.
    /// </summary>
    private AdbException Translate(ProcessResult result, string? serial, string operation)
    {
        var kind = result.TimedOut
            ? AdbErrorKind.Timeout
            : AdbErrorInterpreter.Classify(result.OutputOrError) ?? AdbErrorKind.Unknown;

        var details = $"{operation} (code {result.ExitCode}) : {Truncate(result.OutputOrError)}";

        LogOperationFailed(operation, serial ?? "serveur", details);

        return new AdbException(kind, AdbErrorInterpreter.Describe(kind), details);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Serveur ADB démarré.")]
    private partial void LogServerStarted();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Arrêt du serveur ADB demandé : les autres outils ADB de la machine seront coupés.")]
    private partial void LogServerStopRequested();

    [LoggerMessage(Level = LogLevel.Debug, Message = "ADB > {commandLine}")]
    private partial void LogCommand(string commandLine);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "ADB a échoué (code {exitCode}, expiré : {timedOut}) : {output}")]
    private partial void LogCommandFailed(int exitCode, bool timedOut, string output);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ADB {operation} a échoué pour {target} : {details}")]
    private partial void LogOperationFailed(string operation, string target, string details);

    private static string Truncate(string? text, int max = 800)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : string.Concat(trimmed.AsSpan(0, max), "…");
    }
}

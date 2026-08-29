using DtHub.Core.Adb;
using DtHub.Core.Apps;
using DtHub.Core.Processes;
using DtHub.Core.Scrcpy;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Scrcpy;

/// <summary>
/// Récupère les vrais noms d'applications via <c>scrcpy --list-apps</c>. C'est
/// la seule source pratique : ADB n'expose que des identifiants de ressource.
/// L'opération prend plusieurs secondes, elle est donc mise en cache par
/// appareil.
/// </summary>
public sealed partial class ScrcpyAppLabelProvider : IAppLabelProvider
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _cache = new(StringComparer.Ordinal);

    private readonly IProcessRunner _runner;
    private readonly IScrcpyLocator _scrcpy;
    private readonly IAdbLocator _adb;
    private readonly ILogger<ScrcpyAppLabelProvider> _logger;

    public ScrcpyAppLabelProvider(
        IProcessRunner runner,
        IScrcpyLocator scrcpy,
        IAdbLocator adb,
        ILogger<ScrcpyAppLabelProvider> logger)
    {
        _runner = runner;
        _scrcpy = scrcpy;
        _adb = adb;
        _logger = logger;
    }

    /// <summary>scrcpy annonce lui-même que l'opération peut durer plusieurs secondes.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    public async Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        lock (_cache)
        {
            if (_cache.TryGetValue(serial, out var cached))
            {
                return cached;
            }
        }

        var labels = await ReadLabelsAsync(serial, cancellationToken).ConfigureAwait(false);

        lock (_cache)
        {
            _cache[serial] = labels;
        }

        return labels;
    }

    /// <summary>Force la relecture des noms au prochain appel.</summary>
    public void InvalidateCache(string? serial = null)
    {
        lock (_cache)
        {
            if (serial is null)
            {
                _cache.Clear();
            }
            else
            {
                _cache.Remove(serial);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadLabelsAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);

        string scrcpyPath;
        string adbPath;

        try
        {
            scrcpyPath = await _scrcpy.GetScrcpyPathAsync(cancellationToken).ConfigureAwait(false);
            adbPath = await _adb.GetAdbPathAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnavailable(exception.Message);
            return empty;
        }

        var request = new ProcessRequest
        {
            FileName = scrcpyPath,
            Arguments = ScrcpyCommandBuilder.BuildListAppsArguments(serial),
            Timeout = Timeout,
            Environment = new Dictionary<string, string?> { ["ADB"] = adbPath },
        };

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ProcessLaunchException exception)
        {
            LogUnavailable(exception.Message);
            return empty;
        }

        // scrcpy journalise sur les deux flux selon les versions : on analyse
        // les deux plutôt que de parier sur l'un.
        var apps = AppListParser.ParseScrcpyAppList(
            string.Concat(result.StandardOutput, "\n", result.StandardError));

        if (apps.Count == 0)
        {
            LogNoLabels(serial);
            return empty;
        }

        return apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Label))
            .ToDictionary(app => app.PackageName, app => app.Label!, StringComparer.Ordinal);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Noms d'applications indisponibles ({reason}) ; les noms de paquets seront affichés.")]
    private partial void LogUnavailable(string reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Aucun nom d'application obtenu pour {serial} ; les noms de paquets seront affichés.")]
    private partial void LogNoLabels(string serial);
}

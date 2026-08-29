using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Adb;

/// <summary>
/// Détermine le chemin absolu d'ADB. Dans l'ordre : le chemin explicitement
/// choisi par l'utilisateur, puis la copie installée par DT Hub, qui est
/// téléchargée à la demande. Le PATH n'est jamais consulté, pour ne dépendre
/// d'aucune installation tierce.
/// </summary>
public sealed partial class AdbLocator : IAdbLocator, IDisposable
{
    private readonly IDependencyProvisioner _provisioner;
    private readonly ILogger<AdbLocator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<string?> _overrideProvider;

    private string? _resolved;

    /// <param name="overrideProvider">
    /// Chemin ADB imposé par l'utilisateur dans les paramètres, ou <c>null</c>.
    /// Passé sous forme de fonction pour être relu à chaud après modification.
    /// </param>
    public AdbLocator(
        IDependencyProvisioner provisioner,
        ILogger<AdbLocator> logger,
        Func<string?>? overrideProvider = null)
    {
        _provisioner = provisioner;
        _logger = logger;
        _overrideProvider = overrideProvider ?? (static () => null);
    }

    /// <summary>
    /// Avancement de l'installation d'ADB, à brancher sur l'interface au
    /// premier lancement.
    /// </summary>
    public IProgress<ProvisioningProgress>? Progress { get; set; }

    public async Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default)
    {
        if (_overrideProvider() is { Length: > 0 } custom)
        {
            if (File.Exists(custom))
            {
                return custom;
            }

            LogOverrideMissing(custom);
        }

        if (_resolved is not null)
        {
            return _resolved;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolved is not null)
            {
                return _resolved;
            }

            var dependency = DependencyManifest.Get(DependencyManifest.PlatformToolsKey);

            try
            {
                _resolved = await _provisioner
                    .EnsureAvailableAsync(dependency, Progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DependencyProvisioningException exception)
            {
                throw new AdbException(
                    AdbErrorKind.AdbUnavailable,
                    exception.Message,
                    $"Mise en place de {dependency.DisplayName} {dependency.Version}",
                    exception);
            }

            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le chemin ADB personnalisé {path} est introuvable ; retour à la copie de DT Hub.")]
    private partial void LogOverrideMissing(string path);
}

using DtHub.Core.Dependencies;
using DtHub.Core.Scrcpy;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Infrastructure.Scrcpy;

/// <summary>
/// Provides the path to scrcpy, downloading it from the project's
/// official archive on first need.
/// </summary>
public sealed class ScrcpyLocator : IScrcpyLocator, IDisposable
{
    private readonly IDependencyProvisioner _provisioner;
    private readonly SdlFocusFix _sdlFocusFix;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _resolved;

    public ScrcpyLocator(IDependencyProvisioner provisioner, SdlFocusFix sdlFocusFix)
    {
        _provisioner = provisioner;
        _sdlFocusFix = sdlFocusFix;
    }

    public string Version => DependencyManifest.Get(DependencyManifest.ScrcpyKey).Version;

    public string? TryGetInstalledPath() =>
        _resolved ?? _provisioner.TryGetExistingPath(DependencyManifest.Get(DependencyManifest.ScrcpyKey));

    public async Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolved is null)
            {
                var path = await _provisioner
                    .EnsureAvailableAsync(
                        DependencyManifest.Get(DependencyManifest.ScrcpyKey), progress: null, cancellationToken)
                    .ConfigureAwait(false);

                // Before the first session, so that no scrcpy holds the file yet.
                await _sdlFocusFix.ApplyAsync(path, cancellationToken).ConfigureAwait(false);
                _resolved = path;
            }

            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

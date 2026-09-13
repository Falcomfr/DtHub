using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;

namespace DtHub.Tests.Fakes;

/// <summary>Returns a fixed scrcpy path, without touching the disk.</summary>
public sealed class FakeScrcpyLocator(string path = @"C:\Dev\DTHub\scrcpy\scrcpy.exe") : IScrcpyLocator
{
    public string Path { get; } = path;

    public string Version => "4.1.0";

    /// <summary>
    /// What <see cref="TryGetInstalledPath"/> returns: set, by
    /// default.
    /// </summary>
    public bool IsInstalled { get; set; } = true;

    public string? TryGetInstalledPath() => IsInstalled ? Path : null;

    public Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Path);
}

/// <summary>
/// Simulated application launcher, which records what it is
/// asked.
/// </summary>
public sealed class FakeAppLauncher : IAppLauncher
{
    public sealed record LaunchCall(string Serial, int UserId, string PackageName, string? Component, int? DisplayId);

    public List<LaunchCall> Calls { get; } = [];

    /// <summary>Result returned on every call.</summary>
    public AppLaunchResult Outcome { get; set; } = AppLaunchResult.Success;

    /// <summary>Force-stops requested, in order.</summary>
    public List<string> ForceStops { get; } = [];

    /// <summary>
    /// Called at the precise moment of a force-stop, before it is
    /// recorded.
    ///
    /// Used to observe the state of the world at that instant, which
    /// a list consulted afterwards cannot provide: the order between
    /// scrcpy starting and the game stopping can only be read here.
    /// </summary>
    public Action? OnForceStop { get; set; }

    public Task<AppLaunchResult> LaunchAsync(
        string serial,
        int userId,
        string packageName,
        string? knownComponent,
        int? displayId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new LaunchCall(serial, userId, packageName, knownComponent, displayId));
        return Task.FromResult(Outcome);
    }

    /// <summary>
    /// Addresses the command cannot reach. Used to replay a phone
    /// whose wireless debugging has changed port.
    /// </summary>
    public HashSet<string> Unreachable { get; } = new(StringComparer.Ordinal);

    public Task<bool> ForceStopAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        OnForceStop?.Invoke();
        ForceStops.Add($"{serial}|{userId}|{packageName}");
        return Task.FromResult(!Unreachable.Contains(serial));
    }
}

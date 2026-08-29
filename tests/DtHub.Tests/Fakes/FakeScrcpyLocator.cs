using DtHub.Core.Sessions;
using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Fakes;

/// <summary>Rend un chemin scrcpy fixe, sans toucher au disque.</summary>
public sealed class FakeScrcpyLocator(string path = @"C:\Dev\DTHub\scrcpy\scrcpy.exe") : IScrcpyLocator
{
    public string Path { get; } = path;

    public string Version => "4.1.0";

    public Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Path);
}

/// <summary>Lanceur d'application simulé, qui enregistre ce qu'on lui demande.</summary>
public sealed class FakeAppLauncher : IAppLauncher
{
    public sealed record LaunchCall(string Serial, int UserId, string PackageName, string? Component, int? DisplayId);

    public List<LaunchCall> Calls { get; } = [];

    /// <summary>Résultat rendu à chaque appel.</summary>
    public AppLaunchResult Outcome { get; set; } = AppLaunchResult.Success;

    /// <summary>Arrêts forcés demandés, dans l'ordre.</summary>
    public List<string> ForceStops { get; } = [];

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

    public Task ForceStopAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        ForceStops.Add($"{serial}|{userId}|{packageName}");
        return Task.CompletedTask;
    }
}

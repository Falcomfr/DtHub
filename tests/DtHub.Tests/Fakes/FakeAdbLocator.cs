using DtHub.Core.Adb;

namespace DtHub.Tests.Fakes;

/// <summary>Rend un chemin ADB fixe, sans toucher au disque.</summary>
public sealed class FakeAdbLocator(string path = @"C:\Dev\DTHub\adb\adb.exe") : IAdbLocator
{
    public string Path { get; } = path;

    /// <summary>Ce que rend <see cref="TryGetInstalledPath"/> : posé, par défaut.</summary>
    public bool IsInstalled { get; set; } = true;

    public string? TryGetInstalledPath() => IsInstalled ? Path : null;

    public Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Path);
}

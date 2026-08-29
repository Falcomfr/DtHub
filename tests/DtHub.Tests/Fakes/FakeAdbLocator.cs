using DtHub.Core.Adb;

namespace DtHub.Tests.Fakes;

/// <summary>Rend un chemin ADB fixe, sans toucher au disque.</summary>
public sealed class FakeAdbLocator(string path = @"C:\Dev\DTHub\adb\adb.exe") : IAdbLocator
{
    public string Path { get; } = path;

    public Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Path);
}

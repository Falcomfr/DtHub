using DtHub.Core.Adb;

namespace DtHub.Tests.Fakes;

/// <summary>Returns a fixed ADB path, without touching disk.</summary>
public sealed class FakeAdbLocator(string path = @"C:\Dev\DTHub\adb\adb.exe") : IAdbLocator
{
    public string Path { get; } = path;

    /// <summary>
    /// What <see cref="TryGetInstalledPath"/> returns: set, by default.
    /// </summary>
    public bool IsInstalled { get; set; } = true;

    public string? TryGetInstalledPath() => IsInstalled ? Path : null;

    public Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Path);
}

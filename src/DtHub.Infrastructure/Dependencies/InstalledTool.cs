using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;

namespace DtHub.Infrastructure.Dependencies;

/// <summary>
/// A tool the player installed, found where it lies instead of
/// downloaded. This is the macOS route: the manifest only knows the
/// Windows archives, and Homebrew keeps adb and scrcpy up to date.
///
/// The Homebrew folders come first because an application opened from
/// the Finder does not inherit the shell's PATH.
/// </summary>
public sealed class InstalledTool : IAdbLocator, IScrcpyLocator
{
    private static readonly string[] HomebrewDirectories = ["/opt/homebrew/bin", "/usr/local/bin"];

    private readonly string _name;
    private readonly string _brewPackage;
    private readonly IReadOnlyList<string> _directories;

    public InstalledTool(string name, string brewPackage, IReadOnlyList<string>? directories = null)
    {
        _name = name;
        _brewPackage = brewPackage;
        _directories = directories ??
        [
            .. HomebrewDirectories,
            .. (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries),
        ];
    }

    public string Version => "installed";

    public string? TryGetInstalledPath() =>
        _directories.Select(d => Path.Combine(d, _name)).FirstOrDefault(File.Exists);

    public Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default) =>
        TryGetInstalledPath() is { } path
            ? Task.FromResult(path)
            : throw new AdbException(AdbErrorKind.AdbUnavailable, Missing());

    public Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default) =>
        TryGetInstalledPath() is { } path
            ? Task.FromResult(path)
            : throw new DependencyProvisioningException(_name, Missing());

    private string Missing() => Strings.Format("ToolNotInstalled", _name, _brewPackage);
}

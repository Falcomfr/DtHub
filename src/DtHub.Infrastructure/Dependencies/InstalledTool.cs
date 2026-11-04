using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;

namespace DtHub.Infrastructure.Dependencies;

/// <summary>
/// A tool the player installed, found where it lies instead of downloaded.
/// This is the macOS and Linux route: the manifest only knows the Windows
/// archives. On macOS Homebrew provides adb and scrcpy; on Linux scrcpy's
/// official archive does, with adb inside, since the distributions' scrcpy
/// is often too old for virtual displays.
///
/// The user's own bin folder and Homebrew's come first because an
/// application opened from the Finder or a desktop menu does not always
/// inherit the shell's PATH.
/// </summary>
public sealed class InstalledTool : IAdbLocator, IScrcpyLocator
{
    private readonly string _name;
    private readonly string _installHint;
    private readonly IReadOnlyList<string> _directories;

    /// <param name="installHint">What the player should do, shown after "not found".</param>
    public InstalledTool(string name, string installHint, IReadOnlyList<string>? directories = null)
    {
        _name = name;
        _installHint = installHint;
        _directories = directories ?? DefaultDirectories();
    }

    public string Version => "installed";

    /// <summary>~/.local/bin, Homebrew's folders, then PATH.</summary>
    public static IReadOnlyList<string> DefaultDirectories() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"),
        "/opt/homebrew/bin",
        "/usr/local/bin",
        .. (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries),
    ];

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

    private string Missing() => Strings.Format("ToolNotInstalled", _name, _installHint);
}

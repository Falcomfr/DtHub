namespace DtHub.Core.Updates;

/// <summary>
/// Where an update's files are placed, and when it is allowed to
/// happen.
///
/// The calculation lives here, not in the service that replaces the
/// file: this is the part that decides, and therefore the part that
/// gets tested.
/// </summary>
public static class UpdatePaths
{
    /// <summary>The name of the shipped executable.</summary>
    public const string Executable = "DtHub.exe";

    /// <summary>
    /// What remains of the old executable for the duration of a
    /// restart.
    /// </summary>
    public const string Retired = "DtHub.exe.ancien";

    /// <summary>
    /// True if the currently running executable can be replaced.
    ///
    /// It cannot when it comes from a source tree: the development
    /// launcher republishes on every startup, and an update placed
    /// there would be overwritten a second later by the local build.
    /// Worse, it would look like a regression. The marker is the
    /// solution file, two levels above the publish output.
    /// </summary>
    /// <param name="executablePath">
    /// The full path of the currently running executable.
    /// </param>
    /// <param name="exists">A way to know that a file is there.</param>
    public static bool CanReplace(string? executablePath, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(executablePath);

        for (var at = directory; at is not null; at = Path.GetDirectoryName(at))
        {
            if (exists(Path.Combine(at, "DtHub.slnx")))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The file where the downloaded executable is placed.</summary>
    public static string Staged(string folder, Version version) =>
        Path.Combine(
            folder ?? string.Empty,
            $"DtHub-{Text(version)}.exe");

    /// <summary>
    /// The file where the release notes are placed, next to the
    /// executable.
    ///
    /// It survives the swap and is only read on the following
    /// startup, the one that finally runs the new version: that is
    /// where announcing what changed makes sense.
    /// </summary>
    public static string Notes(string folder, Version version) =>
        Path.Combine(
            folder ?? string.Empty,
            $"notes-{Text(version)}.txt");

    private static string Text(Version? version)
    {
        var normal = ReleaseParser.Normalize(version);

        return $"{normal.Major}.{normal.Minor}.{normal.Build}";
    }
}

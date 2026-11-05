namespace DtHub.Tests;

/// <summary>
/// A test of what only Linux does, such as reading XDG_DATA_HOME, which
/// .NET ignores on a Mac. It runs on Linux and is reported as skipped
/// elsewhere.
/// </summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Linux only.";
        }
    }
}

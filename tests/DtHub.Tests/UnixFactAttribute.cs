namespace DtHub.Tests;

/// <summary>
/// A test of what only exists off Windows, such as Unix file modes. On
/// the Mac it runs like any <see cref="FactAttribute"/>; on Windows it is
/// reported as skipped instead of failed. The mirror of
/// <see cref="WindowsFactAttribute"/>.
/// </summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix only.";
        }
    }
}

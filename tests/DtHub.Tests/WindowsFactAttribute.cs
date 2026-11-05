namespace DtHub.Tests;

/// <summary>
/// A test of Windows-only code, such as the shell's shortcuts or its file
/// locking. On Windows it runs like any <see cref="FactAttribute"/>;
/// elsewhere, as on the Mac, it is reported as skipped instead of failed.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only.";
        }
    }
}

namespace DtHub.Tests;

/// <summary>
/// A test of what only a Mac has, such as ditto and codesign. It runs on a
/// Mac and is reported as skipped elsewhere.
/// </summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "macOS only.";
        }
    }
}

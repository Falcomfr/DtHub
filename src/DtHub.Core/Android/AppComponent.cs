namespace DtHub.Core.Android;

/// <summary>
/// A launchable Android component, in the form <c>package/activity</c>.
/// </summary>
public sealed record AppComponent(string PackageName, string ClassName)
{
    /// <summary>Notation expected by <c>am start -n</c>.</summary>
    public string Value => $"{PackageName}/{ClassName}";

    public override string ToString() => Value;
}

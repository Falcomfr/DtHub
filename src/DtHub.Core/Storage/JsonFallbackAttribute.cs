namespace DtHub.Core.Storage;

/// <summary>
/// Value to fall back on when a configuration file carries an
/// enumeration name this version no longer recognizes.
///
/// Without it, a tier removed from the code made the whole file
/// unreadable: reading failed on that single word alone, and the
/// user lost their instances, their hotkeys and the geometry of
/// their windows. The fallback is declared on the enumeration
/// itself, next to its members, so it comes to mind when editing
/// them.
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class JsonFallbackAttribute(object value) : Attribute
{
    /// <summary>Member kept in place of an unknown name.</summary>
    public object Value { get; } = value;
}

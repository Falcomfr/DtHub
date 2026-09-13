namespace DtHub.Core.Windows;

/// <summary>
/// States whether the tabbed frame should stay where it is.
///
/// An account's lock promises that its window "will no longer move
/// on a stack, a side-by-side or a size change". Once housed in the
/// frame, an account no longer has a geometry of its own: the frame
/// is in charge, and resizing it necessarily resizes everything it
/// holds. The lock therefore protected nothing there, even though
/// the two toggles are independent and an account can very well be
/// both locked and tabbed.
///
/// The rule adopted is the one that keeps the promise: **a single
/// tabbed account that is locked freezes the whole frame**. A lock
/// is a protection, and a neighbor does not lift another's
/// protection. The cost is accepted: a single lock immobilizes the
/// frame for everyone inside it.
/// </summary>
public static class FrameLock
{
    /// <summary>
    /// True if the frame must be left in place by geometry commands.
    /// </summary>
    /// <param name="locked">
    /// Keys of the accounts whose window is locked.
    /// </param>
    /// <param name="tabbed">Keys of the accounts housed in the frame.</param>
    public static bool Freezes(IEnumerable<string> locked, IEnumerable<string> tabbed)
    {
        ArgumentNullException.ThrowIfNull(locked);
        ArgumentNullException.ThrowIfNull(tabbed);

        var verrouilles = locked as ISet<string> ?? new HashSet<string>(locked, StringComparer.Ordinal);

        return tabbed.Any(verrouilles.Contains);
    }
}

namespace DtHub.Core.Windows;

/// <summary>
/// The game windows in the list's order, Android and iOS together: what
/// the shortcuts and the arranging commands go through.
/// </summary>
public static class GameWindowList
{
    /// <summary>
    /// The windows the automatic placements move: alive, neither locked
    /// (<paramref name="unmanaged"/>) nor housed in the frame (<paramref name="tabbed"/>).
    /// </summary>
    /// <param name="order">The account's rank in the list; a stable sort keeps the given order among equals.</param>
    public static IReadOnlyList<IGameWindow> Managed(
        IEnumerable<IGameWindow> windows,
        IReadOnlySet<string> unmanaged,
        IReadOnlySet<string> tabbed,
        Func<string, int> order)
    {
        ArgumentNullException.ThrowIfNull(unmanaged);
        ArgumentNullException.ThrowIfNull(tabbed);

        return Accounts(windows, order).Where(w => !unmanaged.Contains(w.Key) && !tabbed.Contains(w.Key)).ToList();
    }

    /// <summary>
    /// The open accounts, one per line of the list, locked and housed ones
    /// included. Windows opened in passing are left out by the caller.
    /// </summary>
    public static IReadOnlyList<IGameWindow> Accounts(IEnumerable<IGameWindow> windows, Func<string, int> order)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(order);

        return [.. windows.Where(w => w.IsAlive).OrderBy(w => order(w.Key))];
    }
}

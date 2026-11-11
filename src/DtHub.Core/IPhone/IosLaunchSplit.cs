using DtHub.Core.Dofus;

namespace DtHub.Core.IPhone;

/// <summary>
/// A launch's accounts in two: the Android ones go the scrcpy way, the iOS
/// ones open through their device's stream, and a missing iPhone holds no
/// one back.
/// </summary>
public static class IosLaunchSplit
{
    /// <summary>Both parts keep the list's order.</summary>
    public static (IReadOnlyList<DofusInstance> Android, IReadOnlyList<DofusInstance> Ios) Split(IEnumerable<DofusInstance> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var all = accounts.ToList();

        return ([.. all.Where(a => !a.IsIos)], [.. all.Where(a => a.IsIos)]);
    }

    /// <summary>The keys of the iOS accounts whose device is not listed.</summary>
    public static IReadOnlyList<string> Missing(IEnumerable<DofusInstance> ios, IReadOnlySet<string> listedUdids)
    {
        ArgumentNullException.ThrowIfNull(ios);
        ArgumentNullException.ThrowIfNull(listedUdids);

        return [.. ios.Where(a => !listedUdids.Contains(a.DeviceId)).Select(a => a.Key)];
    }

    /// <summary>
    /// The list is searched again before opening: only the panel's sweep
    /// fills it, so hidden at startup or for a team's shortcut it may be
    /// empty or old.
    /// </summary>
    public static bool NeedsListing(IEnumerable<DofusInstance> ios, IReadOnlySet<string> listedUdids, bool listFailed) =>
        listFailed || Missing(ios, listedUdids).Count > 0;
}

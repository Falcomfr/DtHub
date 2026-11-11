using DtHub.Core.Dofus;
using DtHub.Core.Sessions;

namespace DtHub.Core.IPhone;

/// <summary>How an iOS account's row reads, from its device and its window's life.</summary>
public static class IosAccountRow
{
    /// <summary>The account key of the device's one account.</summary>
    public static string KeyOf(string udid) => $"{udid}|0|{DofusInstance.IosPackage}";

    /// <summary>Listed, or unplugged while its window waits for it in place.</summary>
    public static bool Shows(string udid, IReadOnlySet<string> listed, IosWindowState? state)
    {
        ArgumentNullException.ThrowIfNull(listed);
        return listed.Contains(udid) || state is not null;
    }

    /// <summary>
    /// False for a listed device below iOS 27, which refuses the stream. An
    /// unlisted one is left to the launcher, which says it is not plugged in.
    /// </summary>
    public static bool CanOpen(IPhoneDevice? listed) => listed?.CanMirror != false;

    /// <summary>
    /// A device forgotten while plugged in stays forgotten until a listing that
    /// worked no longer shows it: a failed one says nothing about it.
    /// </summary>
    public static void KeepForgotten(ISet<string> forgotten, IEnumerable<string> listed, bool listingFailed)
    {
        ArgumentNullException.ThrowIfNull(forgotten);

        if (!listingFailed)
        {
            forgotten.IntersectWith(listed);
        }
    }

    /// <summary>
    /// A window waiting for its device or reopening after a failure is still
    /// a window the row must offer to close; a given-up one opens afresh.
    /// </summary>
    public static InstanceActivity Activity(IosWindowState? state) => state switch
    {
        IosWindowState.Open => InstanceActivity.Running,
        IosWindowState.Opening or IosWindowState.Unplugged or IosWindowState.Failed => InstanceActivity.Starting,
        _ => InstanceActivity.Idle,
    };

    /// <summary>The status line's resource key and whether it warns, or <c>null</c> without a window.</summary>
    public static (string Key, bool Warning)? Status(IosWindowState? state, bool gameMissing) => state switch
    {
        IosWindowState.Open when gameMissing => ("IosGameMissing", true),
        IosWindowState.Open => ("IosOpen", false),
        IosWindowState.Opening => ("IosOpening", false),
        IosWindowState.Unplugged => ("IosUnplugged", true),
        IosWindowState.Failed or IosWindowState.Closed => ("IosFailed", true),
        _ => null,
    };
}

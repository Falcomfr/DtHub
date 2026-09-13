namespace DtHub.Core.Dofus;

/// <summary>
/// An account whose window is open, and the name it displays.
/// </summary>
/// <param name="Key">Instance key.</param>
/// <param name="UserName">Android profile name, the fallback name.</param>
/// <param name="Shown">Name currently shown on the tab or the title.</param>
public readonly record struct OpenInstance(string Key, string UserName, string Shown);

/// <summary>
/// What must be rewritten when an account changes name.
///
/// **The defect this type exists to catch.** A tab's label was a copy
/// of the name taken at the moment the window was docked into the
/// frame, and nothing wrote it again afterwards. Renaming an account
/// did write the setting, and the window kept the old name until it
/// was reopened. Two accounts renamed on the same day did not share
/// the same fate: the one whose window had been opened after its
/// renaming looked correct, and made the other one look like a
/// special case.
///
/// **Why the computation lives here and not in the service that sets
/// the titles.** This is the part that decides, so the part we put to
/// the test. Setting a title on a Windows window cannot be tested;
/// knowing which one, it can.
///
/// **And why it compares before returning.** The settings event fires
/// on every write, including the geometry of a window being moved,
/// which is to say often. Returning every account every time would
/// rewrite the titles of every open window for a single mouse
/// movement.
/// </summary>
public static class InstanceRenames
{
    /// <summary>
    /// Open accounts whose displayed name no longer matches the
    /// settings, and the name they must now carry.
    ///
    /// Returns an empty dictionary when nothing has moved, which is
    /// the ordinary case.
    /// </summary>
    /// <param name="open">Accounts whose window is open.</param>
    /// <param name="customNameFor">
    /// The name chosen by the user for this key, as the settings now
    /// carry it, or <c>null</c> if there is none.
    /// </param>
    public static Dictionary<string, string> Pending(
        IEnumerable<OpenInstance> open,
        Func<string, string?> customNameFor)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(customNameFor);

        var pending = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var instance in open)
        {
            var wanted = DofusInstance.NameOf(customNameFor(instance.Key), instance.UserName);

            if (!string.Equals(wanted, instance.Shown, StringComparison.Ordinal))
            {
                pending[instance.Key] = wanted;
            }
        }

        return pending;
    }
}

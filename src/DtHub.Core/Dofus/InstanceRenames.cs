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
    /// The name an account must carry right now.
    ///
    /// **The one place the rule lives.** A surface that shows a name
    /// derives it from here when it is born, and never from a copy taken
    /// at launch. The defect this type was created for came back through
    /// the other door: the label was fixed when an account was renamed,
    /// but a tab opened after the renaming was still seeded from the name
    /// frozen when scrcpy started, so renaming a free window and then
    /// docking it showed the old name for the rest of the session.
    /// </summary>
    /// <param name="key">Instance key.</param>
    /// <param name="userName">Android profile name, the fallback name.</param>
    /// <param name="customNameFor">
    /// The name chosen by the user for this key, as the settings now
    /// carry it, or <c>null</c> if there is none.
    /// </param>
    public static string NameNow(string key, string userName, Func<string, string?> customNameFor)
    {
        ArgumentNullException.ThrowIfNull(customNameFor);

        return DofusInstance.NameOf(customNameFor(key), userName);
    }

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
            var wanted = NameNow(instance.Key, instance.UserName, customNameFor);

            if (!string.Equals(wanted, instance.Shown, StringComparison.Ordinal))
            {
                pending[instance.Key] = wanted;
            }
        }

        return pending;
    }
}

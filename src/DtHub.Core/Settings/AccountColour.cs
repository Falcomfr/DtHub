using DtHub.Core.Storage;

namespace DtHub.Core.Settings;

/// <summary>
/// The colour that tells one account from another.
///
/// **Nothing distinguished two accounts but a name in twelve pixels.**
/// Playing four at once means four windows that look alike, and the
/// only way to know which was which was to read the title. The colour
/// marks the row, its tab in the frame, and the window itself.
///
/// Six hues, named for what they look like and not for a role. A role
/// would be a lie the first time someone reordered their accounts: a
/// colour means nothing except "this one, not that one".
///
/// The fallback has a consequence worth stating, because it surprises:
/// a name this version does not know becomes a real colour, not
/// <c>null</c>. A hand-edited file with a typo in it therefore comes
/// back with the wrong colour rather than with none, which is the right
/// trade for a field where any colour beats a lost file.
/// </summary>
[JsonFallback(Teal)]
public enum AccountColour
{
    /// <summary>Blue-green, the furthest from the accent that is still cool.</summary>
    Teal,

    /// <summary>Green, well clear of the success indicator's own.</summary>
    Moss,

    /// <summary>Warm brown-gold, not the warning indicator's yellow.</summary>
    Brass,

    /// <summary>Cool violet.</summary>
    Indigo,

    /// <summary>Warm violet.</summary>
    Orchid,

    /// <summary>Dusty pink, well clear of the danger indicator's red.</summary>
    Rose,

    /// <summary>
    /// No mark, because that is what the user asked for.
    ///
    /// **This exists because two absences were being written the same
    /// way.** An account that had never been given a colour and an
    /// account someone had deliberately stripped both read as
    /// <c>null</c>, so the application could either hand a colour back
    /// to the second, or leave the first blank for good. It chose the
    /// second, and two accounts on a real installation stayed unmarked
    /// with nothing able to repair them.
    ///
    /// Now <c>null</c> means "nobody has decided", which the application
    /// settles at the next sweep, and <c>None</c> means "decided, and the
    /// answer is no", which it never overrides.
    /// </summary>
    None,
}

namespace DtHub.Core.Users;

/// <summary>
/// Nature of an Android user. It is inferred from the flags reported
/// by <c>pm list users</c>, refined by <c>dumpsys user</c> when that
/// one responds. Nothing is inferred from the identifier: a clone is
/// not always 999, nor is a work profile always 10.
/// </summary>
public enum AndroidUserType
{
    /// <summary>Unknown or unreadable flags.</summary>
    Unknown = 0,

    /// <summary>Primary user of the phone.</summary>
    Primary,

    /// <summary>
    /// Managed profile. Android files under this type both a work
    /// profile and an app duplication feature, depending on the
    /// manufacturer's overlay, hence a neutral label.
    /// </summary>
    ManagedProfile,

    /// <summary>App cloning profile.</summary>
    CloneProfile,

    /// <summary>Second space or full secondary user.</summary>
    Secondary,

    Guest,

    Restricted,
}

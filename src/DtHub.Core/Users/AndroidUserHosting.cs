using DtHub.Core.Localization;

namespace DtHub.Core.Users;

/// <summary>
/// What an Android profile is allowed to put on screen while the other
/// profiles are open.
///
/// The rule is not an opinion: it comes from a measurement made on a
/// Xiaomi 23078PND5G running Android 16, using a virtual display
/// created by scrcpy, by querying the oracle Android itself publishes,
/// <c>cmd user is-user-visible --display D N</c>.
///
/// <list type="table">
///   <item>
///     <term>Managed profile, id 15, flags 0x1030</term>
///     <description>visible: true. The game opens in four seconds,
///     <c>LaunchState: COLD</c>, full login screen.</description>
///   </item>
///   <item>
///     <term>Full user, id 14, flags 0x400</term>
///     <description>visible: false. <c>am start</c> nonetheless
///     replies <c>Status: ok</c>, then hangs for seventy seconds
///     showing nothing.</description>
///   </item>
/// </list>
///
/// This is the reason this file exists: <c>am start</c> reports
/// success where nothing will ever display. Trusting it amounts to
/// promising a window that will never come.
///
/// A profile always follows its parent: as soon as the primary user is
/// visible, its profiles are too, on any display. A full user, on the
/// other hand, can only be visible in the background if the device
/// allows it, which is what
/// <c>cmd user is-visible-background-users-supported</c> says: false
/// on an ordinary phone, true on automotive embedded systems.
/// </summary>
public static class AndroidUserHosting
{
    /// <summary>
    /// States whether this profile can host a game window while the
    /// others are open, and if not, why.
    /// </summary>
    /// <param name="user">The profile being examined.</param>
    /// <param name="visibleBackgroundUsers">
    /// What the device answers to
    /// <c>is-visible-background-users-supported</c>. <c>null</c> when
    /// the question was not asked or did not succeed: we then default
    /// to the behavior of ordinary phones.
    /// </param>
    public static AndroidUserHosting.Verdict Describe(
        AndroidUser user,
        bool? visibleBackgroundUsers = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Pause takes precedence over type: a paused work profile
        // launches nothing, whatever its nature otherwise.
        if (user.IsPaused)
        {
            return new Verdict(
                false,
                Strings.Get("ProfilePausedShort"));
        }

        switch (user.Type)
        {
            case AndroidUserType.Primary:
            case AndroidUserType.ManagedProfile:
            case AndroidUserType.CloneProfile:
                return new Verdict(true, string.Empty);

            case AndroidUserType.Secondary:
                return visibleBackgroundUsers == true
                    ? new Verdict(true, string.Empty)
                    : new Verdict(
                        false,
                        Strings.Get("OneFullUserAtATime"));

            case AndroidUserType.Guest:
                return new Verdict(
                    false,
                    Strings.Get("GuestProfileWiped"));

            case AndroidUserType.Restricted:
                return new Verdict(
                    false,
                    Strings.Get("RestrictedProfile"));

            default:
                return new Verdict(
                    false,
                    Strings.Get("UnknownProfileKind"));
        }
    }

    /// <summary>
    /// Result of the rule. <see cref="Reason"/> is empty when the
    /// profile is suitable, and otherwise carries a sentence that can
    /// be shown as is.
    /// </summary>
    public readonly record struct Verdict(bool CanHostWindow, string Reason);
}

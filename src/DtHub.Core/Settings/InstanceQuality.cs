namespace DtHub.Core.Settings;

/// <summary>
/// Which quality tier applies to which account.
///
/// Until now there was only one for everyone. That is the wrong
/// setting for the case at hand: you play one account and watch
/// four. The main account deserves sixty frames and a high bitrate;
/// the mules do not need that, and what is spared on them is that
/// much less processor, bandwidth, heat and battery.
///
/// **The rule fits in one sentence: the account overrides the
/// shared setting.** There is no third level. A launch profile
/// appears to carry one, but it copies its values into the shared
/// setting before launch: by the time the question arises, only two
/// sources remain.
///
/// **Polling rates do not follow.** A tier also carries the polling
/// rate, and those belong to the application, not to a window:
/// polling devices at five different rates because five accounts are
/// open would make no sense. Only the resolution and the bitrate are
/// set per account.
/// </summary>
public static class InstanceQuality
{
    /// <summary>
    /// The tier that applies: the account's if it has one, otherwise
    /// the shared one.
    /// </summary>
    public static StreamQuality Chosen(StreamQuality? instance, StreamQuality shared) => instance ?? shared;

    /// <summary>
    /// The full profile that applies to an account.
    /// </summary>
    /// <param name="instance">
    /// Account tier, or <c>null</c> to follow the shared one.
    /// </param>
    /// <param name="shared">Shared tier.</param>
    /// <param name="sharedCustom">Shared fine tuning.</param>
    /// <remarks>
    /// There is no per-account fine tuning, and that is deliberate:
    /// it would be a persisted field that nothing would expose. An
    /// account chooses a tier among the ones that exist, or follows
    /// the shared one. The fine values of the custom tier remain
    /// shared.
    /// </remarks>
    public static QualityProfile ProfileFor(
        StreamQuality? instance,
        StreamQuality shared,
        CustomQuality? sharedCustom) =>
        QualityProfile.For(Chosen(instance, shared), sharedCustom);
}

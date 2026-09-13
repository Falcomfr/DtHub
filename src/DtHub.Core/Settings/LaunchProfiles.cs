using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Settings;

/// <summary>
/// The rules for named sessions, with no state and no input or output.
///
/// A profile is a list of accounts, kept apart from the startup set.
/// The distinction is the whole point: this set changes shape with
/// every action, a successful launch adding the opened accounts to it
/// and the "close" button removing them. A profile derived on the fly
/// from this set would therefore rewrite itself, and would have kept
/// nothing at all.
/// </summary>
public static class LaunchProfiles
{
    /// <summary>
    /// Beyond this, the name overflows the drop-down list. Truncating
    /// is better than refusing: nobody counts letters while typing.
    /// </summary>
    public const int MaxNameLength = 40;

    /// <summary>
    /// Number of accounts named in a summary before counting them
    /// instead.
    /// </summary>
    private const int NamedAtMost = 3;

    /// <summary>
    /// The name as it will be kept, or <c>null</c> if it is not one.
    ///
    /// Edge spaces are dropped: "Duo" and "Duo  " are the same profile,
    /// and letting the difference through would create two that could
    /// not be told apart on screen.
    /// </summary>
    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    /// <summary>
    /// What the profiles button carries.
    ///
    /// The name of the profile selected for startup when there is
    /// one, the generic word otherwise. Nothing used to distinguish
    /// "no named session" from "Duo high quality will apply at the
    /// next startup", and the bubble had to be opened to find out. Yet
    /// a profile decides which accounts open, the quality, the
    /// distance and the anchoring: we ourselves spent a long time
    /// looking for why quality went back to maximum at every launch,
    /// for lack of anything showing it.
    ///
    /// The name goes through <see cref="Normalize" />, which discards
    /// edge spaces and an empty name: a button that would show
    /// nothing but blank would be worse than the generic word.
    /// </summary>
    public static string ButtonLabel(string? defaultProfile) =>
        Normalize(defaultProfile) ?? Strings.Get("Profiles");

    /// <summary>
    /// The profile with this name, or <c>null</c>.
    ///
    /// Case is ignored: "Duo" and "duo" would be two indistinguishable
    /// entries in the list, and one would no longer know which one is
    /// being opened. Ordinal, not culture aware: a profile name is an
    /// identifier, not text to sort, and falling back from one
    /// culture to another has no business here.
    /// </summary>
    public static StoredLaunchProfile? Find(
        IEnumerable<StoredLaunchProfile>? profiles,
        string? name)
    {
        if (profiles is null || Normalize(name) is not { } wanted)
        {
            return null;
        }

        return profiles.FirstOrDefault(
            p => string.Equals(p.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The accounts of this profile that still exist.
    ///
    /// A profile can name an account since removed from the phone.
    /// Discarding it rather than failing is the only workable
    /// behavior: the profile keeps its purpose, and the remaining
    /// accounts open.
    /// </summary>
    public static IReadOnlyList<string> KeysFor(
        IEnumerable<StoredLaunchProfile>? profiles,
        string? name,
        IEnumerable<StoredInstance>? instances)
    {
        if (Find(profiles, name) is not { } profile || profile.InstanceKeys is null)
        {
            return [];
        }

        var known = new HashSet<string>(
            instances?.Select(i => i.Key) ?? [], StringComparer.Ordinal);

        return [.. profile.InstanceKeys.Where(known.Contains).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// What the profile contains, in one showable line.
    ///
    /// Names for as long as they fit, a count beyond that: "XSpace +
    /// Main" reads well, "A + B + C + D + E" no longer does.
    /// </summary>
    public static string Describe(
        StoredLaunchProfile? profile,
        IEnumerable<StoredInstance>? instances)
    {
        if (profile?.InstanceKeys is not { Count: > 0 })
        {
            return Strings.Get("NoAccount");
        }

        List<StoredInstance> all = [.. instances ?? []];

        List<string> names = [.. profile.InstanceKeys
            .Select(key => all.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal)))
            .Where(i => i is not null)
            .Select(i => NameOf(i!))];

        if (names.Count == 0)
        {
            return Strings.Get("NoKnownAccount");
        }

        var accountsText = names.Count <= NamedAtMost
            ? string.Join(" + ", names)
            : Strings.Format("AccountCount", names.Count);

        // Quality is only stated if it is out of the ordinary:
        // repeating it for every profile would drown out the account
        // names, which is what we are looking for.
        return profile.Quality == StreamQuality.Medium
            ? accountsText
            : $"{accountsText}, {QualityLabel(profile.Quality)}";
    }

    /// <summary>
    /// What a profile is about to keep, said before creating it.
    ///
    /// "Save" gave no hint of what left along with the name: people
    /// thought only accounts were being kept, and opening the profile
    /// would reposition windows and change the quality. The sentence
    /// says it in advance, with the current values rather than a
    /// fixed list, so that people recognize their own settings.
    /// </summary>
    public static string Announce(
        int accounts,
        StreamQuality quality,
        GameZoom zoom,
        int tabbed,
        bool audio)
    {
        // The article lives in each language's template, not in the
        // label: "la qualité haute" ("the high quality") in French,
        // "high quality" with no article in English. Attaching the
        // article to the word made the sentence untranslatable, since
        // gender is not the same from one language to another.
        var accountsText = accounts <= 1
            ? Strings.Get("ProfileAccountsOne")
            : Strings.Format("ProfileAccountsMany", accounts);

        var phrase = Strings.Format(
            "ProfileAnnounce",
            accountsText,
            QualityLabel(quality),
            ZoomLabel(zoom),
            Strings.Get(audio ? "SoundToPc" : "SoundOff"));

        if (tabbed <= 0)
        {
            return phrase;
        }

        var onglets = tabbed <= 1
            ? Strings.Get("ProfileTabbedOne")
            : Strings.Format("ProfileTabbedMany", tabbed);

        return $"{phrase} {onglets}";
    }

    /// <summary>
    /// The word for distance, as it appears in the panel.
    /// </summary>
    private static string ZoomLabel(GameZoom zoom) => zoom switch
    {
        GameZoom.Widest => Strings.Get("ZoomVeryFarWord"),
        GameZoom.Wide => Strings.Get("ZoomFarWord"),
        GameZoom.Close => Strings.Get("ZoomCloseWord"),
        _ => Strings.Get("ZoomNormalWord"),
    };

    /// <summary>
    /// The word for the quality tier, as it appears in the panel.
    /// </summary>
    private static string QualityLabel(StreamQuality quality) => quality switch
    {
        StreamQuality.Low => Strings.Get("QualityLowWord"),
        StreamQuality.Maximum => Strings.Get("QualityHighWord"),
        StreamQuality.Custom => Strings.Get("QualityCustomWord"),
        _ => Strings.Get("QualityMediumWord"),
    };

    /// <summary>
    /// The name chosen by the user, or the Android profile's name
    /// otherwise.
    /// </summary>
    private static string NameOf(StoredInstance instance) =>
        string.IsNullOrWhiteSpace(instance.CustomName)
            ? instance.UserName
            : instance.CustomName.Trim();
}

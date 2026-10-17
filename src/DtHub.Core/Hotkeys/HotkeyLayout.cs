using DtHub.Core.Localization;

namespace DtHub.Core.Hotkeys;

/// <summary>The headings of the shortcuts editor.</summary>
public enum HotkeySection
{
    /// <summary>Going from one account to another.</summary>
    Switching,

    /// <summary>Arranging the game windows.</summary>
    Arranging,

    /// <summary>DT Hub's own windows, the phone, quitting.</summary>
    Application,
}

/// <summary>
/// How the shortcuts editor lays the actions out: three sections, and the
/// numbered series folded into one line each. Twenty-two lines one under
/// the other, F1 to F8 each on its own, were hard to read (2026-10-16).
/// </summary>
public static class HotkeyLayout
{
    /// <summary>
    /// The sections in order, each with its entries in order. An entry of
    /// more than one action is a series, shown as one line.
    /// </summary>
    public static IReadOnlyList<(HotkeySection Section, IReadOnlyList<IReadOnlyList<HotkeyAction>> Entries)> Sections { get; } =
    [
        (HotkeySection.Switching,
        [
            [HotkeyAction.NextInstance],
            [HotkeyAction.PreviousInstance],
            Run(HotkeyAction.Account1, HotkeyAction.Account8),
        ]),
        (HotkeySection.Arranging,
        [
            [HotkeyAction.Fullscreen],
            [HotkeyAction.Rearrange],
            [HotkeyAction.Tile],
            [HotkeyAction.Grid],
            Run(HotkeyAction.Size1, HotkeyAction.Size4),
        ]),
        (HotkeySection.Application,
        [
            [HotkeyAction.ToggleConfigurator],
            [HotkeyAction.Quests],
            [HotkeyAction.Almanax],
            [HotkeyAction.ScreenOff],
            [HotkeyAction.Quit],
        ]),
    ];

    /// <summary>The section's heading.</summary>
    public static string Title(HotkeySection section) => section switch
    {
        HotkeySection.Switching => Strings.Get("HotkeySectionSwitching"),
        HotkeySection.Arranging => Strings.Get("HotkeySectionArranging"),
        _ => Strings.Get("HotkeySectionApplication"),
    };

    /// <summary>The label of a series' folded line.</summary>
    public static string SeriesLabel(IReadOnlyList<HotkeyAction> series) =>
        series[0] == HotkeyAction.Account1
            ? Strings.Format("HotkeySeriesAccounts", series.Count)
            : Strings.Format("HotkeySeriesSizes", series.Count);

    /// <summary>
    /// "F1 … F8" when the keys still follow one another, with the same
    /// modifiers; <c>null</c> as soon as one is changed or removed, where
    /// the summary would lie about it.
    /// </summary>
    public static string? RunText(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (bindings.Count == 0 || bindings.Any(b => !b.IsAssigned))
        {
            return null;
        }

        for (var i = 1; i < bindings.Count; i++)
        {
            if (bindings[i].Modifiers != bindings[0].Modifiers
                || bindings[i].VirtualKey != bindings[0].VirtualKey + i)
            {
                return null;
            }
        }

        return $"{bindings[0].DisplayText} … {bindings[^1].DisplayText}";
    }

    private static HotkeyAction[] Run(HotkeyAction first, HotkeyAction last) =>
        [.. Enumerable.Range(0, last - first + 1).Select(i => first + i)];
}

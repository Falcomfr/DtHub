using DtHub.Core.Localization;

namespace DtHub.Core.Windows;

/// <summary>
/// The five window sizes. The first four are percentages of the
/// screen's usable area, so proportional to it: the same size 2
/// gives a small window on a laptop screen and a large one on a
/// desktop screen. The fifth takes up the whole screen, with no
/// border.
/// </summary>
public sealed record WindowSizePresets
{
    public static readonly WindowSizePresets Default = new();

    /// <summary>Percentages of the first four sizes, in order.</summary>
    public IReadOnlyList<int> Percentages { get; init; } = [40, 60, 80, 100];

    /// <summary>Total number of sizes, full screen included.</summary>
    public int Count => Percentages.Count + 1;

    /// <summary>Index of the full screen size, the last one.</summary>
    public int FullscreenIndex => Percentages.Count;

    /// <summary>
    /// True if the index designates the borderless full screen.
    /// </summary>
    public bool IsFullscreen(int index) => index == FullscreenIndex;

    /// <summary>
    /// Percentage associated with an index. An out-of-range index
    /// falls back to the nearest value: a misconfigured shortcut
    /// must not break the display.
    /// </summary>
    public int PercentageAt(int index) =>
        Percentages.Count == 0 ? 100 : Percentages[Math.Clamp(index, 0, Percentages.Count - 1)];

    /// <summary>
    /// Returns a sanitized copy: percentages brought back into
    /// reasonable bounds, sorted, deduplicated, and never empty.
    /// </summary>
    public WindowSizePresets Sanitized()
    {
        var cleaned = Percentages
            .Select(p => Math.Clamp(p, 20, 100))
            .Distinct()
            .Order()
            .ToList();

        return cleaned.Count == 0 ? Default : this with { Percentages = cleaned };
    }
}

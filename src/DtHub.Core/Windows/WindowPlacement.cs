using System.Text.Json.Serialization;

namespace DtHub.Core.Windows;

/// <summary>
/// Where an application window is, as Windows remembers it.
///
/// The rectangle is in desktop pixels, not in WPF coordinates: the
/// latter depend on the scaling of the screen that carries the
/// window, so that the same number does not designate the same
/// spot from one screen to another. Across two screens with
/// different densities, this is the only way to recover the exact
/// place.
/// </summary>
public sealed record WindowPlacement
{
    public int Left { get; init; }

    public int Top { get; init; }

    public int Right { get; init; }

    public int Bottom { get; init; }

    /// <summary>True if the window was maximized.</summary>
    public bool Maximized { get; init; }

    /// <summary>
    /// True if the rectangle has an area. A window that was never
    /// shown returns an empty one, which must be neither saved nor
    /// applied.
    ///
    /// Kept out of the file: this is a reading of the four edges,
    /// not a value to store.
    /// </summary>
    [JsonIgnore]
    public bool IsSized => Right > Left && Bottom > Top;
}

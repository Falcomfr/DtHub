namespace DtHub.Core.Guidance;

/// <summary>
/// A drawn settings screen: its title, the row you tap on it, and the
/// height at which that row is drawn.
/// </summary>
/// <param name="Title">The name of the screen you are on.</param>
/// <param name="Tap">
/// The row to tap to go further. Empty on the only screen of a path
/// that has just one, where there is nothing to tap.
/// </param>
/// <param name="Row">Rank of this row among those drawn.</param>
public readonly record struct MenuScreen(string Title, string Tap, int Row);

/// <summary>
/// What dresses up a silent row of a drawn screen.
///
/// None of this names an actual setting: the brand sheet gives only
/// the path. These are the traits every Android settings list has,
/// and without them the drawing looked like any other list.
/// </summary>
/// <param name="BarShare">
/// Share of the width taken up by the label's bar.
/// </param>
/// <param name="Tint">Rank of the badge's tint, from 0 to five.</param>
/// <param name="HasSwitch">
/// True when the row carries a switch, not a chevron.
/// </param>
/// <param name="HasSubtitle">
/// True when a second, shorter line follows it.
/// </param>
public readonly record struct MenuRowDecor(
    double BarShare,
    int Tint,
    bool HasSwitch,
    bool HasSubtitle);

/// <summary>
/// Splits a menu path into screens, to show it rather than have it
/// read.
///
/// Brand sheets already write "Paramètres › Applications › DOFUS
/// Touch", and that is the sequence of screens to go through: there
/// is nothing more to write, only to read it.
///
/// A path of N segments gives N-1 screens, not N: you are *in*
/// "Paramètres" and you tap "Applications" there. The last segment is
/// the row to tap on the last screen, not one more screen, which
/// would be drawn empty.
/// </summary>
public static class MenuPath
{
    /// <summary>Number of rows drawn in each screen.</summary>
    public const int Rows = 5;

    private const char Separator = '›';

    /// <summary>
    /// The screens of the path, in order. An empty path gives none,
    /// and the view then shows nothing rather than a hollow frame.
    /// </summary>
    public static IReadOnlyList<MenuScreen> Screens(string? path)
    {
        var segments = Segments(path);

        if (segments.Count == 0)
        {
            return [];
        }

        // A single segment: the screen to go to is shown, with no row
        // to tap, because there is none.
        if (segments.Count == 1)
        {
            return [new MenuScreen(segments[0], string.Empty, 0)];
        }

        List<MenuScreen> screens = new(segments.Count - 1);
        var previous = -1;

        for (var i = 0; i + 1 < segments.Count; i++)
        {
            var tap = segments[i + 1];
            var row = RowFor(tap);

            // Two screens in a row whose line sits at the same height
            // give an image that looks frozen. It is shifted, which
            // is enough to make the drawing look like distinct
            // screens.
            if (row == previous)
            {
                row = (row + 1) % Rows;
            }

            screens.Add(new MenuScreen(segments[i], tap, row));
            previous = row;
        }

        return screens;
    }

    private static List<string> Segments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        List<string> segments = [];

        foreach (var part in path.Split(Separator))
        {
            var label = part.Trim();

            if (label.Length > 0)
            {
                segments.Add(label);
            }
        }

        return segments;
    }

    /// <summary>Number of badge tints.</summary>
    public const int Tints = 6;

    /// <summary>
    /// The dressing of a silent row.
    ///
    /// Bars all the same length, badges all the same color and not a
    /// single switch: the drawing gave itself away, no settings list
    /// looks like that. So everything is uneven, but nothing is drawn
    /// at random: the same screen must draw itself the same way every
    /// time the window opens, or the illustration would shift under
    /// the eyes of whoever rereads it.
    /// </summary>
    /// <param name="title">The name of the screen.</param>
    /// <param name="row">The rank of the row.</param>
    public static MenuRowDecor Decor(string? title, int row)
    {
        var ligne = Hash((row + 1) * 7, title);

        return new MenuRowDecor(
            // Five widths, far enough apart to be seen, close enough
            // for the list to stay a list.
            0.5 + (ligne % 5 * 0.115),
            ligne / 5 % Tints,
            // One single switch per screen. A first attempt drew one
            // per row, and "Applications" ended up with four toggles:
            // a screen you only pass through does not carry four.
            row == Hash(0, title) % Rows,
            // A subtitle one time in three: many settings announce
            // their state under their name, but not all of them.
            ligne / 97 % 3 == 0);
    }

    /// <summary>
    /// A stable fingerprint of a title. Stable and not random: the
    /// same screen must draw itself the same way every time the
    /// window opens, or the illustration would shift under the eyes
    /// of whoever rereads it.
    /// </summary>
    private static int Hash(int seed, string? title)
    {
        var sum = seed;

        foreach (var character in title ?? string.Empty)
        {
            sum = ((sum * 31) + character) % 65536;
        }

        return sum;
    }

    /// <summary>
    /// The row's height, derived from its label.
    ///
    /// Derived, not drawn at random: the same screen must draw itself
    /// the same way every time the window opens, or the illustration
    /// would shift under the eyes of whoever rereads it.
    /// </summary>
    private static int RowFor(string label)
    {
        var sum = 0;

        foreach (var character in label)
        {
            sum = ((sum * 31) + character) % 1024;
        }

        return sum % Rows;
    }
}

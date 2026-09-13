namespace DtHub.Core.Windows;

/// <summary>
/// Rectangle in screen pixels. Defined here rather than borrowing a
/// WPF type: layout logic must remain testable without a graphical
/// interface.
/// </summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int CenterX => X + (Width / 2);

    public int CenterY => Y + (Height / 2);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Width-to-height ratio, or zero if the rectangle is empty.
    /// </summary>
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>
    /// Area of the rectangle, as a <c>long</c>: a 4K screen would
    /// overflow an <c>int</c>.
    /// </summary>
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>
    /// Common part of two rectangles, empty if they do not touch.
    /// </summary>
    public ScreenRect Intersect(ScreenRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return right <= left || bottom <= top
            ? default
            : new ScreenRect(left, top, right - left, bottom - top);
    }

    public override string ToString() => $"{Width}x{Height} en ({X}, {Y})";
}

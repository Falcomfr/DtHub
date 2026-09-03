namespace DtHub.Core.Windows;

/// <summary>
/// Rectangle en pixels écran. Défini ici plutôt que d'emprunter un type WPF :
/// la logique de disposition doit rester testable sans interface graphique.
/// </summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int CenterX => X + (Width / 2);

    public int CenterY => Y + (Height / 2);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Rapport largeur sur hauteur, ou zéro si le rectangle est vide.</summary>
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Aire du rectangle, en <c>long</c> : un écran 4K la ferait déborder d'un <c>int</c>.</summary>
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>Partie commune à deux rectangles, vide s'ils ne se touchent pas.</summary>
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

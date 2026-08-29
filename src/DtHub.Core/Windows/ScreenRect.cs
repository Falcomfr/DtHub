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

    public override string ToString() => $"{Width}x{Height} en ({X}, {Y})";
}

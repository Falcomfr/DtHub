namespace DtHub.Core.Papycha;

/// <summary>
/// Range les donjons par palier de cinquante niveaux.
///
/// Quatre-vingt-trois donjons de douze à deux cents ne se parcourent pas d'un
/// œil : on y cherche ce qui est à sa portée. Les paliers coupent la liste comme
/// les succès coupent celle des quêtes, sans changer l'ordre.
///
/// Fonction pure : elle se vérifie aux bornes, qui sont le seul endroit où l'on
/// se trompe.
/// </summary>
public static class DungeonLevelBand
{
    /// <summary>Largeur d'un palier.</summary>
    private const int Width = 50;

    /// <summary>
    /// Rang du palier d'un niveau, à partir de zéro. Rend <see cref="Unknown"/>
    /// quand le site ne renseigne pas de niveau.
    /// </summary>
    public static int RankOf(int level) =>
        level <= 0 ? Unknown : (level - 1) / Width;

    /// <summary>Rang des donjons sans niveau, qui ferment la marche.</summary>
    public static int Unknown => int.MaxValue;

    /// <summary>Intitulé du palier, « Niveau 51 à 100 ».</summary>
    public static string NameOf(int level)
    {
        var rank = RankOf(level);

        if (rank == Unknown)
        {
            return "Niveau inconnu";
        }

        var first = (rank * Width) + 1;

        return $"Niveau {first} à {first + Width - 1}";
    }
}

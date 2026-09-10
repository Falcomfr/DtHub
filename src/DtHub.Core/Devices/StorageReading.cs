using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// Ce que l'appareil dit de sa place libre.
///
/// Le manque de place est une cause documentée de plantages de DOFUS Touch,
/// et elle est trompeuse : le jeu ne dit pas qu'il manque de place, il ferme.
///
/// **Honnêtement, c'est une assurance plus qu'un besoin.** L'appareil de
/// référence a près de trois cents gigaoctets libres, et l'avertissement n'y
/// paraîtra jamais. Il coûte une lecture et une analyse, et il sert le jour où
/// quelqu'un joue sur un téléphone plein.
/// </summary>
/// <param name="FreeBytes">Octets libres sur la partition des données.</param>
public sealed record StorageReading(long FreeBytes)
{
    /// <summary>En dessous, le jeu peut échouer sans dire pourquoi.</summary>
    public const long Low = 2L * 1024 * 1024 * 1024;

    /// <summary>En dessous, il échouera.</summary>
    public const long Critical = 512L * 1024 * 1024;

    /// <summary>La place libre en gigaoctets, arrondie au dixième.</summary>
    public double FreeGigabytes => Math.Round(FreeBytes / (1024.0 * 1024 * 1024), 1);

    /// <summary>Vrai quand la place mérite qu'on en parle.</summary>
    public bool IsLow => FreeBytes <= Low;

    /// <summary>
    /// Lit la sortie de <c>df /data</c>. Rend <c>null</c> dès que la colonne
    /// manque : ne rien savoir est un cas ordinaire.
    ///
    /// Relevé sur l'appareil de référence, en blocs d'un kilooctet :
    ///
    /// <code>
    /// Filesystem       1K-blocks      Used Available Use% Mounted on
    /// /dev/block/dm-59 485636064 171563720 313535476  36% /data/user/0
    /// </code>
    ///
    /// La colonne se cherche par son intitulé et non par son rang : le nom du
    /// volume peut contenir un espace, et compter les colonnes depuis la
    /// gauche décalerait tout. On lit donc l'en-tête pour savoir où regarder,
    /// en comptant **depuis la droite**, la fin des lignes étant régulière.
    /// </summary>
    public static StorageReading? Parse(string? df)
    {
        if (string.IsNullOrWhiteSpace(df))
        {
            return null;
        }

        var lines = df.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
        {
            return null;
        }

        var header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var column = Array.FindIndex(header, h => h.Equals("Available", StringComparison.OrdinalIgnoreCase));

        if (column < 0)
        {
            return null;
        }

        // Depuis la droite : « Mounted on » compte pour deux mots dans
        // l'en-tête et pour un chemin dans la ligne, et le nom du volume peut
        // contenir un espace. Le rang depuis la fin, lui, est stable.
        var fromEnd = header.Length - column;

        foreach (var line in lines[1..])
        {
            var cells = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (cells.Length < fromEnd)
            {
                continue;
            }

            // L'en-tête compte « Mounted on » pour deux mots, la ligne pour un
            // seul chemin : un cran d'écart, toujours le même.
            var at = cells.Length - fromEnd + 1;

            if (at >= 0
                && at < cells.Length
                && long.TryParse(cells[at], NumberStyles.None, CultureInfo.InvariantCulture, out var blocks))
            {
                return new StorageReading(blocks * 1024L);
            }
        }

        return null;
    }

    /// <summary>Ce qu'il y a à dire, ou <c>null</c> quand il n'y a rien à dire.</summary>
    public string? Describe() => FreeBytes <= Critical
        ? Strings.Format("DeviceStorageCritical", FreeGigabytes)
        : IsLow
            ? Strings.Format("DeviceStorageLow", FreeGigabytes)
            : null;
}

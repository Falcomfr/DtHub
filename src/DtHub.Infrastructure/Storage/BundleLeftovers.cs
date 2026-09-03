using System.Diagnostics;

namespace DtHub.Infrastructure.Storage;

/// <summary>
/// Les dossiers laissés dans %TEMP% par les versions précédentes.
///
/// Le fichier unique compresse ses composants managés et les décompresse en
/// mémoire, mais les six bibliothèques natives de WPF et l'amorce WebView2
/// exigent un vrai chemin sur disque. L'hôte les pose donc sous
/// %TEMP%\.net\DtHub\{identifiant}, où l'identifiant est recalculé à chaque
/// publication. Une mise à jour laisse ainsi le dossier de la version d'avant
/// derrière elle, huit mégaoctets chacun, et rien ne les reprend jamais :
/// mesuré à cent soixante et un dossiers pour un giga-octet et trois cents
/// mégaoctets sur le poste de développement.
///
/// Le README affirmait que supprimer le fichier ne laissait rien : c'était vrai
/// du dossier de données, faux de celui-ci.
/// </summary>
public static class BundleLeftovers
{
    /// <summary>
    /// Le dossier où l'hôte pose les bibliothèques natives : c'est celui d'une
    /// bibliothèque effectivement chargée, et non un chemin recomposé.
    /// </summary>
    private static readonly string Mark =
        $"{Path.DirectorySeparatorChar}.net{Path.DirectorySeparatorChar}";

    /// <summary>
    /// Dossier d'extraction de la version en cours, ou <c>null</c> hors fichier
    /// unique. En développement rien n'est extrait, et il n'y a rien à balayer.
    ///
    /// La reconnaissance ne vise aucune bibliothèque en particulier : elles se
    /// chargent à la demande, et viser wpfgfx_cor3.dll ne trouvait rien tant
    /// que la première fenêtre n'avait pas été dessinée.
    /// </summary>
    public static string? CurrentDirectory()
    {
        using var self = Process.GetCurrentProcess();

        foreach (ProcessModule module in self.Modules)
        {
            using (module)
            {
                if (module.FileName is { } file
                    && file.Contains(Mark, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(file);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Les dossiers frères à retirer : tous sauf celui de la version en cours.
    /// La comparaison est celle du système de fichiers de Windows, qui ne
    /// distingue pas la casse.
    /// </summary>
    public static IReadOnlyList<string> Stale(IEnumerable<string> siblings, string current)
    {
        ArgumentNullException.ThrowIfNull(siblings);

        return siblings
            .Where(directory => !string.Equals(
                directory.TrimEnd('\\'), current?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Retire ce que les versions précédentes ont laissé. Rend le nombre de
    /// dossiers effacés.
    ///
    /// Un dossier encore utilisé résiste de lui-même : Windows refuse d'effacer
    /// une bibliothèque chargée. L'échec est donc avalé, il ne dit rien d'autre
    /// que « pas celui-ci ».
    /// </summary>
    public static int Sweep()
    {
        if (CurrentDirectory() is not { } current)
        {
            return 0;
        }

        var root = Path.GetDirectoryName(current);
        if (root is null || !Directory.Exists(root))
        {
            return 0;
        }

        var removed = 0;

        foreach (var directory in Stale(Directory.EnumerateDirectories(root), current))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Dossier d'une instance encore vivante, ou verrouillé par
                // l'antivirus : il attendra le prochain démarrage.
            }
        }

        return removed;
    }
}

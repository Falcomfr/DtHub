namespace DtHub.Core.Updates;

/// <summary>
/// Où se posent les fichiers d'une mise à jour, et quand elle a le droit de se
/// faire.
///
/// Le calcul est ici, et non dans le service qui remplace le fichier : c'est la
/// partie qui décide, donc celle qu'on éprouve.
/// </summary>
public static class UpdatePaths
{
    /// <summary>Le nom de l'exécutable livré.</summary>
    public const string Executable = "DtHub.exe";

    /// <summary>Ce qui reste de l'ancien exécutable le temps d'un redémarrage.</summary>
    public const string Retired = "DtHub.exe.ancien";

    /// <summary>
    /// Vrai si l'exécutable en cours peut être remplacé.
    ///
    /// Il ne le peut pas quand il sort d'un arbre de sources : le lanceur de
    /// développement republie à chaque démarrage, et une mise à jour posée là
    /// serait écrasée à la seconde suivante par la compilation locale. Pire,
    /// elle ferait croire à une régression. La marque est le fichier de
    /// solution, deux niveaux au-dessus de la sortie de publication.
    /// </summary>
    /// <param name="executablePath">Le chemin complet de l'exécutable en cours.</param>
    /// <param name="exists">De quoi savoir qu'un fichier est là.</param>
    public static bool CanReplace(string? executablePath, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(executablePath);

        for (var at = directory; at is not null; at = Path.GetDirectoryName(at))
        {
            if (exists(Path.Combine(at, "DtHub.slnx")))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Le fichier où se pose l'exécutable téléchargé.</summary>
    public static string Staged(string folder, Version version) =>
        Path.Combine(
            folder ?? string.Empty,
            $"DtHub-{Text(version)}.exe");

    /// <summary>
    /// Le fichier où se pose la note de version, à côté de l'exécutable.
    ///
    /// Elle survit à l'échange et n'est lue qu'au démarrage suivant, celui qui
    /// exécute enfin la nouvelle version : c'est là qu'annoncer ce qui change a
    /// un sens.
    /// </summary>
    public static string Notes(string folder, Version version) =>
        Path.Combine(
            folder ?? string.Empty,
            $"notes-{Text(version)}.txt");

    private static string Text(Version? version)
    {
        var normal = ReleaseParser.Normalize(version);

        return $"{normal.Major}.{normal.Minor}.{normal.Build}";
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace DtHub.Core.Updates;

/// <summary>
/// Rend lisible la note de version, qui arrive dans le langage de balisage du
/// dépôt.
///
/// Aucune bibliothèque pour cela : la note est une liste à puces et deux ou
/// trois titres, et embarquer un moteur de rendu complet pour l'afficher
/// coûterait plus que ce qu'il rapporte. Ce qui n'est pas reconnu est laissé
/// tel quel, ce qui est le pire cas acceptable : on lit le texte brut.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>La note, débarrassée de son balisage.</summary>
    public static string Readable(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var lines = notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var built = new StringBuilder();
        var blank = 0;

        foreach (var raw in lines)
        {
            var line = Clean(raw);

            if (line.Length == 0)
            {
                blank++;

                continue;
            }

            if (built.Length > 0)
            {
                _ = built.Append('\n');

                // Un seul blanc de séparation : le dépôt en met deux ou trois
                // entre ses blocs, ce qui trouerait un panneau de quelques
                // lignes.
                if (blank > 0)
                {
                    _ = built.Append('\n');
                }
            }

            blank = 0;
            _ = built.Append(line);
        }

        return built.ToString();
    }

    private static string Clean(string raw)
    {
        var line = raw.TrimEnd();

        // Les titres perdent leurs dièses, non leur texte.
        line = HeadingPattern().Replace(line, string.Empty);

        // Une puce devient une puce.
        line = BulletPattern().Replace(line, "•  ");

        // Le gras, l'italique et le code n'ont pas de rendu ici : leurs marques
        // gêneraient la lecture plus qu'elles ne l'aideraient.
        line = EmphasisPattern().Replace(line, "$1");
        line = CodePattern().Replace(line, "$1");

        // Un lien garde son texte et perd son adresse.
        line = LinkPattern().Replace(line, "$1");

        return line.Trim();
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*", RegexOptions.None, 2000)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+", RegexOptions.None, 2000)]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"\*{1,3}([^*]+)\*{1,3}", RegexOptions.None, 2000)]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex(@"`([^`]+)`", RegexOptions.None, 2000)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.None, 2000)]
    private static partial Regex LinkPattern();
}

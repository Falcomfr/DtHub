namespace DtHub.Tests.Conventions;

/// <summary>
/// Le tiret cadratin est proscrit dans tout le dépôt, code et documents
/// compris. C'est une consigne d'écriture, donc précisément le genre de règle
/// qu'on enfreint sans s'en apercevoir : vingt-deux occurrences s'étaient
/// glissées dans neuf fichiers, dont une dans le rapport de diagnostic,
/// c'est-à-dire dans un texte que les gens collent en public.
///
/// Le contrôle porte sur le texte des fichiers, comme celui des commandes du
/// XAML et celui des blocs catch.
/// </summary>
public class TypographyTests
{
    // Le point de code plutôt que le caractère : écrit tel quel, ce contrôle
    // échouerait sur lui-même.
    private const char Cadratin = '\u2014';

    /// <summary>
    /// Fichiers qui ne portent pas notre écriture.
    ///
    /// La sonde des étapes garde sur disque une copie des guides du site et le
    /// détail de ce qu'elle en retient. C'est la prose de quelqu'un d'autre,
    /// recopiée telle quelle : la corriger serait fausser la mesure, et la
    /// juger n'a pas de sens puisque cette consigne porte sur ce que nous
    /// écrivons. Les deux fichiers ne sont pas versionnés.
    /// </summary>
    private static readonly string[] Copies = ["corpus.json", "etapes.json"];

    private static readonly string[] Lus =
        [".cs", ".xaml", ".md", ".yml", ".yaml", ".json", ".csproj", ".props", ".slnx",
         ".ps1", ".py", ".cmd", ".js", ".resx"];

    [Fact]
    public void Aucun_tiret_cadratin_nulle_part()
    {
        List<string> fautifs = [];

        foreach (var file in Sources())
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(Cadratin, StringComparison.Ordinal))
                {
                    fautifs.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.Equal([], fautifs.Order());
    }

    private static IEnumerable<string> Sources()
    {
        var root = RepositoryRoot.Path();
        var separator = Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                || file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                || file.Contains($"{separator}.git{separator}", StringComparison.Ordinal)
                || Copies.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                continue;
            }

            if (Lus.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }
}

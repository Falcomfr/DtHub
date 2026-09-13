namespace DtHub.Tests.Conventions;

/// <summary>
/// The em dash is forbidden throughout the repository, code and
/// documents included. This is a writing rule, and therefore exactly
/// the kind of rule that gets broken without anyone noticing:
/// twenty-two occurrences had slipped into nine files, including one
/// in the diagnostic report, that is, in a text that people paste in
/// public.
///
/// The check covers the text of the files, as well as that of XAML
/// commands and that of catch blocks.
/// </summary>
public class TypographyTests
{
    // The code point rather than the character: written as is, this
    // check would fail on itself.
    private const char Cadratin = '\u2014';

    /// <summary>
    /// Files that do not carry our writing.
    ///
    /// The step probe keeps a disk copy of the site's guides and the
    /// detail of what it retains from them. This is someone else's
    /// prose, copied verbatim: correcting it would skew the
    /// measurement, and judging it makes no sense since this rule
    /// concerns what we write. The two files are not
    /// version-controlled.
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

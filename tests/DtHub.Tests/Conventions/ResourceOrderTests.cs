using System.Text.RegularExpressions;

namespace DtHub.Tests.Conventions;

/// <summary>
/// Every key a theme dictionary reaches for with <c>StaticResource</c>
/// must already have been merged when that dictionary is read.
///
/// **This was paid for.** A button template in the controls dictionary
/// compared two values through a converter declared in the converters
/// dictionary, which was merged after it. Everything compiled, every
/// test passed, and the application died while laying out the account
/// panel: "Impossible de trouver la ressource nommée 'SameValue'". A
/// StaticResource is resolved as the file is read, so the order of the
/// merged dictionaries is part of the code and not a matter of taste.
///
/// Read as text, like the other checks here: the test assembly targets
/// no Windows framework and cannot load a WPF dictionary.
/// </summary>
public class ResourceOrderTests
{
    private static readonly string Themes =
        Path.Combine(RepositoryRoot.Path(), "src", "DtHub.App", "Themes");

    /// <summary>
    /// The dictionaries in the order App.xaml merges them, which is the
    /// order they become visible in.
    /// </summary>
    private static List<string> MergeOrder()
    {
        var app = File.ReadAllText(
            Path.Combine(RepositoryRoot.Path(), "src", "DtHub.App", "App.xaml"));

        return [.. Regex
            .Matches(app, """<ResourceDictionary Source="Themes/([\w.]+)" />""")
            .Select(m => m.Groups[1].Value)];
    }

    private static HashSet<string> KeysOf(string file) =>
        [.. Regex
            .Matches(File.ReadAllText(Path.Combine(Themes, file)), """" x:Key="([^"{]+)" """".Trim())
            .Select(m => m.Groups[1].Value)];

    private static IEnumerable<string> UsesOf(string file) =>
        Regex
            .Matches(File.ReadAllText(Path.Combine(Themes, file)), """\{StaticResource ([^}{]+)\}""")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(name => !name.StartsWith('{'))
            .Distinct(StringComparer.Ordinal);

    [Fact]
    public void Les_dictionnaires_fusionnes_sont_bien_les_trois_attendus()
    {
        Assert.Equal(["PaletteDark.xaml", "Converters.xaml", "Controls.xaml"], MergeOrder());
    }

    [Fact]
    public void Aucun_dictionnaire_ne_reclame_une_ressource_fusionnee_apres_lui()
    {
        var order = MergeOrder();
        HashSet<string> visible = new(StringComparer.Ordinal);

        foreach (var file in order)
        {
            // Its own keys count: within one file the reader is already
            // past the declaration by the time a template uses it, which
            // is how the palette brushes and the type scale work.
            visible.UnionWith(KeysOf(file));

            foreach (var used in UsesOf(file))
            {
                Assert.True(
                    visible.Contains(used),
                    $"{file} demande « {used} », qui n'est fusionné qu'après lui.");
            }
        }
    }
}

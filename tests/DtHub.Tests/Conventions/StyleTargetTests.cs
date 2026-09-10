using System.Text.RegularExpressions;

namespace DtHub.Tests.Conventions;

/// <summary>
/// Un style nommé doit viser le type de l'élément qui le porte.
///
/// WPF ne le vérifie qu'à l'exécution, et il ne pardonne pas : appliquer un
/// style de <c>Button</c> à un <c>ToggleButton</c> lève au moment où la
/// fenêtre se construit, et l'application meurt en emportant sa pile. C'est
/// arrivé, sur une bascule de note à laquelle un style d'icône avait été
/// donné : la liste des comptes s'est vidée et le processus s'est arrêté sur
/// un débordement de pile, dont la cause n'apparaissait qu'au fond du journal.
///
/// La compilation ne dit rien, et aucune épreuve ne pouvait le dire non plus,
/// les épreuves n'atteignant pas la couche d'interface. Celle-ci lit donc le
/// texte des fichiers, comme le contrôle du tiret cadratin.
/// </summary>
public partial class StyleTargetTests
{
    [Fact]
    public void Chaque_style_nomme_vise_le_type_qui_le_porte()
    {
        var root = RepositoryRoot.Path();

        var targets = Targets(Path.Combine(root, "src", "DtHub.App", "Themes"));

        Assert.NotEmpty(targets);

        List<string> wrong = [];

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src", "DtHub.App"), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (Match applied in Applied().Matches(text))
            {
                var element = applied.Groups[1].Value;
                var key = applied.Groups[2].Value;

                // Un style qu'on ne trouve pas est défini ailleurs, dans la
                // fenêtre elle-même par exemple : on ne juge que ce qu'on voit.
                if (targets.TryGetValue(key, out var target)
                    && !string.Equals(element, target, StringComparison.Ordinal))
                {
                    wrong.Add($"{Path.GetFileName(file)} : <{element}> porte « {key} », qui vise {target}.");
                }
            }
        }

        Assert.Equal([], wrong);
    }

    /// <summary>Les styles nommés des thèmes, et le type que chacun vise.</summary>
    private static Dictionary<string, string> Targets(string themes)
    {
        Dictionary<string, string> found = new(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(themes, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match declared in Declared().Matches(File.ReadAllText(file)))
            {
                found[declared.Groups[1].Value] = declared.Groups[2].Value;
            }
        }

        return found;
    }

    // Les styles dérivés d'un autre ne sont pas jugés ici : « BasedOn » impose
    // déjà la compatibilité des types, et WPF la vérifie à la compilation.
    [GeneratedRegex(@"<Style\s+x:Key=""(\w+)""\s+TargetType=""(\w+)""")]
    private static partial Regex Declared();

    [GeneratedRegex(@"<(\w+)\b[^>]*?Style=""\{StaticResource (\w+)\}""", RegexOptions.Singleline)]
    private static partial Regex Applied();
}

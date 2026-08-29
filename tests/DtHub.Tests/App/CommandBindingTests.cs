using System.Text.RegularExpressions;

namespace DtHub.Tests.App;

/// <summary>
/// Vérifie que chaque bouton de l'interface est relié à une commande qui
/// existe vraiment.
///
/// WPF ne signale rien quand une commande est introuvable : le bouton reste
/// cliquable et ne fait rien. Trois boutons de l'onglet Appareils sont restés
/// morts pour cette raison, sans le moindre message. Le contrôle est fait sur
/// le texte des fichiers, faute de quoi il faudrait référencer le projet
/// d'interface et basculer toute la suite de tests sur Windows.
/// </summary>
public sealed class CommandBindingTests
{
    private static readonly Regex BoundCommand = new(
        """Command\s*=\s*"\{Binding\s+(?<path>[^,}]+)""",
        RegexOptions.Singleline);

    [Fact]
    public void Chaque_commande_liee_dans_le_XAML_existe_dans_un_view_model()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "DtHub.App");
        var sources = string.Concat(Directory
            .EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        List<string> missing = [];

        foreach (var view in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match match in BoundCommand.Matches(File.ReadAllText(view)))
            {
                // « DataContext.RestartCommand » ou « RestartCommand » : seul
                // le dernier segment nomme la commande.
                var name = match.Groups["path"].Value.Trim().Split('.')[^1].Trim();

                if (!name.EndsWith("Command", StringComparison.Ordinal) || Declares(sources, name))
                {
                    continue;
                }

                missing.Add($"{Path.GetFileName(view)} : {name}");
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Vrai si la commande est déclarée à la main, ou engendrée par
    /// <c>[RelayCommand]</c> à partir d'une méthode du même nom, avec ou sans
    /// le suffixe <c>Async</c> que la génération retire.
    /// </summary>
    private static bool Declares(string sources, string name)
    {
        var method = name[..^"Command".Length];

        return sources.Contains(name, StringComparison.Ordinal)
            || Regex.IsMatch(
                sources,
                $@"\[RelayCommand[^\]]*\][\s\S]{{0,400}}?\b{Regex.Escape(method)}(Async)?\s*\(");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DtHub.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }
}

using System.Text.RegularExpressions;

namespace DtHub.Tests.Diagnostics;

/// <summary>
/// Une erreur attrapée doit ressortir : par le journal, par l'écran, par un
/// échec rendu à l'appelant, ou par un commentaire qui dit pourquoi le silence
/// est le bon choix.
///
/// La convention est déjà écrite dans AGENTS.md, « pas de catch (Exception)
/// muet ». Un relevé l'a trouvée enfreinte trente et une fois, dont dix-huit
/// pour de bon : les treize autres remontaient l'erreur d'une façon que le
/// relevé ne connaissait pas. C'est pourquoi cette épreuve énumère les façons
/// de remonter plutôt que de chercher un seul mot.
///
/// Le contrôle est fait sur le texte des fichiers, comme celui des commandes du
/// XAML : il faudrait sinon référencer le projet d'interface et basculer toute
/// la suite sur Windows.
/// </summary>
public sealed class CatchDisciplineTests
{
    private static readonly Regex Opens = new(@"^\s*catch\b");

    /// <summary>
    /// Les façons dont une erreur ressort d'un bloc. Journaliser, la montrer,
    /// la rendre sous forme d'échec nommé, ou la relancer.
    /// </summary>
    private static readonly Regex Speaks = new(
        @"Log[A-Za-z]*\(|Log\.\w+\(|_logger|Report\(|throw|"
        + @"Problem\s*=|Status\s*=|FailureKind\s*=|FailureMessage\s*=|"
        + @"Show(Warning|Information|Error)|"
        + @"FailedSession|AppLaunchResult\.Failure|AccountAddition\(|"
        + @"\.Record\(|TrySetException|ThrowIfCancellationRequested|ReportViewFailure|"
        + @"Note\(");

    [Fact]
    public void Aucune_erreur_n_est_avalee_sans_un_mot()
    {
        List<string> muets = [];

        foreach (var (file, lines) in Sources())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Opens.IsMatch(lines[i]) || Speaks.IsMatch(Body(lines, i)))
                {
                    continue;
                }

                // Un commentaire, dans le bloc ou juste au-dessus, vaut
                // décision assumée : c'est ce que la convention demande.
                var above = string.Join('\n', lines[Math.Max(0, i - 4)..i]);

                if (!Body(lines, i).Contains("//", StringComparison.Ordinal)
                    && !above.Contains("//", StringComparison.Ordinal))
                {
                    muets.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.Equal([], muets.Order());
    }

    /// <summary>
    /// Attraper toute exception sans filtre cache les fautes qu'on ne veut pas
    /// traiter, à commencer par le manque de mémoire.
    ///
    /// La dérogation ne se déclare plus par un couple de coordonnées, mais par
    /// une phrase écrite sous le bloc. L'épreuve affirmait
    /// « Win32HotkeyRegistrar.cs:228 » : insérer une ligne ailleurs dans ce
    /// fichier la faisait rougir alors qu'elle ne parlait pas de ce qu'on avait
    /// modifié. Ancrée sur la phrase, elle devient aussi plus forte : un
    /// nouveau bloc sans filtre n'y échappe qu'en écrivant la même décision
    /// délibérée, à l'endroit où on la lira.
    /// </summary>
    /// <summary>La phrase qui déclare une dérogation assumée, sous le bloc.</summary>
    private const string Derogation = "Sans filtre, et c'est voulu";

    [Fact]
    public void Attraper_tout_se_borne_par_un_filtre()
    {
        List<string> larges = [];

        foreach (var (file, lines) in Sources())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s*catch\s*\(\s*Exception\b"))
                {
                    continue;
                }

                // Le filtre s'écrit parfois à la ligne suivante.
                var window = lines[i] + " " + (i + 1 < lines.Length ? lines[i + 1] : string.Empty);

                if (window.Contains(" when ", StringComparison.Ordinal)
                    || Body(lines, i).Contains(Derogation, StringComparison.Ordinal))
                {
                    continue;
                }

                larges.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.Equal([], larges.Order());
    }

    /// <summary>Le corps du bloc, jusqu'à son accolade fermante.</summary>
    private static string Body(string[] lines, int at)
    {
        var indent = lines[at].Length - lines[at].TrimStart().Length;
        List<string> body = [];

        for (var i = at + 1; i < Math.Min(lines.Length, at + 16); i++)
        {
            if (lines[i].Trim() == "}" && lines[i].Length - lines[i].TrimStart().Length == indent)
            {
                break;
            }

            body.Add(lines[i]);
        }

        return string.Join('\n', body);
    }

    private static IEnumerable<(string File, string[] Lines)> Sources()
    {
        var root = Path.Combine(RepositoryRoot.Path(), "src");

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            yield return (file, File.ReadAllLines(file));
        }
    }

}

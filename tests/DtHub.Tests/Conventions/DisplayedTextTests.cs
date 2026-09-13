using System.Text.RegularExpressions;

namespace DtHub.Tests.Conventions;

/// <summary>
/// No sentence meant for the screen may be hard-coded in a view model.
///
/// A French sentence placed directly in the code breaks nothing and does
/// not show up as an error: it just displays as is, in French, to an
/// English speaker as much as to a Spanish speaker. An audit found nine
/// of them, including "Connexion en cours..." ("Connecting..."), "Compte
/// {n}" ("Account {n}"), the three frame-rate labels of the quality
/// setting, and the title of the "Ajouter un compte" ("Add an account")
/// box, which already had its own translation key.
///
/// **The check does not look for accented letters.** That was the first
/// idea, and it let five of the nine through: "Connexion en cours"
/// carries none. It looks for sentences instead, meaning two words or
/// more outside the format holes. An identifier, a file name, an
/// address, or an interpolated string do not count as two.
///
/// View models only: that is where text meant for the screen is built.
/// Elsewhere, French is intentional, in the logs, the diagnostic report,
/// and programming mistakes, and a check that swept the whole repository
/// would drown in it.
/// </summary>
public partial class DisplayedTextTests
{
    /// <summary>
    /// What is made of two words without being a sentence.
    ///
    /// The date format reads like text but is meant for <c>ToString</c>,
    /// and it is the displayed culture that translates it.
    /// </summary>
    private static readonly string[] Tolerees = ["MMMM yyyy", "Français", "Español"];

    [Fact]
    public void Aucune_phrase_en_dur_dans_une_vue_modele()
    {
        List<string> fautifs = [];

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot.Path(), "src", "DtHub.App", "ViewModels"),
            "*.cs",
            SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            var inLogger = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Log templates stay in French: only we ever read them,
                // and a resource would needlessly weigh them down. Their
                // text often sits on the following line, hence tracking
                // through to the closing token rather than a line by
                // line check, which used to let a call written across
                // three lines slip through.
                if (line.Contains("[LoggerMessage", StringComparison.Ordinal)
                    || line.Contains("Log.", StringComparison.Ordinal))
                {
                    inLogger = true;
                }

                if (inLogger)
                {
                    inLogger = !line.EndsWith(")]", StringComparison.Ordinal)
                        && !line.EndsWith(");", StringComparison.Ordinal);
                    continue;
                }

                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('*'))
                {
                    continue;
                }

                foreach (Match literal in Litteral().Matches(line))
                {
                    var text = literal.Value.Trim('"');

                    if (EstUnePhrase(text))
                    {
                        fautifs.Add($"{Path.GetFileName(file)}:{i + 1} {text}");
                    }
                }
            }
        }

        Assert.Equal([], fautifs.Order());
    }

    private static bool EstUnePhrase(string text) =>
        !Tolerees.Contains(text, StringComparer.Ordinal)
        && !text.Contains("://", StringComparison.Ordinal)
        && Mot().Count(Trou().Replace(text, " ")) >= 2;

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
    private static partial Regex Litteral();

    /// <summary>
    /// A format hole does not count: it is not translated.
    /// </summary>
    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Trou();

    [GeneratedRegex(@"[A-Za-zÀ-ÿ]{3,}")]
    private static partial Regex Mot();
}

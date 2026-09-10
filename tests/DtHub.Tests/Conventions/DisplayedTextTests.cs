using System.Text.RegularExpressions;

namespace DtHub.Tests.Conventions;

/// <summary>
/// Aucune phrase destinée à l'écran ne doit être écrite en dur dans une
/// vue-modèle.
///
/// Une phrase française posée directement dans le code ne casse rien et ne se
/// voit pas : elle s'affiche telle quelle, en français, à un anglophone comme
/// à un hispanophone. Un audit en a trouvé neuf, dont « Connexion en cours… »,
/// « Compte {n} », les trois cadences du réglage de qualité et le titre de la
/// boîte « Ajouter un compte », qui avait pourtant déjà sa clé de traduction.
///
/// **Le contrôle ne cherche pas les accents.** C'était la première idée, et
/// elle laissait passer cinq des neuf : « Connexion en cours » n'en porte
/// aucun. Il cherche donc les phrases, c'est-à-dire deux mots ou plus hors des
/// trous de format. Un identifiant, un nom de fichier, une adresse ou une
/// chaîne d'interpolation n'en font pas deux.
///
/// Les vues-modèles seules : c'est là que le texte destiné à l'écran se
/// fabrique. Ailleurs, le français est voulu, dans les journaux, le rapport de
/// diagnostic et les fautes de programmation, et un contrôle qui ratisserait
/// tout le dépôt s'y noierait.
/// </summary>
public partial class DisplayedTextTests
{
    /// <summary>
    /// Ce qui est fait de deux mots sans être une phrase.
    ///
    /// Le format de date se lit comme du texte mais s'adresse à
    /// <c>ToString</c>, et c'est la culture affichée qui le traduit.
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

                // Les modèles de journal restent en français : ils ne sont
                // lus que par nous, et une ressource les alourdirait pour
                // rien. Leur texte tient souvent sur la ligne suivante, d'où
                // le suivi jusqu'à la fermeture plutôt qu'un contrôle ligne à
                // ligne, qui laissait passer un appel écrit sur trois lignes.
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

    /// <summary>Un trou de format ne compte pas : il ne se traduit pas.</summary>
    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Trou();

    [GeneratedRegex(@"[A-Za-zÀ-ÿ]{3,}")]
    private static partial Regex Mot();
}

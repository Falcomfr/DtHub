using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DtHub.Core.Localization;

namespace DtHub.Tests.Localization;

/// <summary>
/// Garde les trois fichiers de traduction alignés.
///
/// Une clé oubliée dans une langue ne casse rien à la compilation : elle se
/// rend telle quelle à l'écran, et personne ne s'en aperçoit avant qu'un
/// hispanophone ne voie « QualityHigh » dans un bouton. Le contrôle est fait
/// sur le texte des fichiers, comme pour les commandes du XAML.
/// </summary>
public sealed class StringsResourceTests
{
    private static readonly Regex UsedInXaml = new(@"\{loc:T\s+(?<key>[A-Za-z0-9_]+)\s*\}");
    private static readonly Regex UsedInCode = new(@"Strings\.(?:Get|Format)\(\s*""(?<key>[A-Za-z0-9_]+)""");

    [Fact]
    public void Les_trois_langues_portent_les_memes_cles()
    {
        var neutre = Keys(string.Empty);

        Assert.NotEmpty(neutre);

        foreach (var langue in AppLanguage.Supported.Where(l => l != AppLanguage.Neutral))
        {
            var traduites = Keys(langue);

            Assert.Equal([], neutre.Except(traduites).Order());
            Assert.Equal([], traduites.Except(neutre).Order());
        }
    }

    [Fact]
    public void Aucun_texte_n_est_vide()
    {
        foreach (var langue in AppLanguage.Supported)
        {
            foreach (var (key, value) in Entries(langue == AppLanguage.Neutral ? string.Empty : langue))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(value),
                    $"{key} est vide en « {langue} ».");
            }
        }
    }

    /// <summary>
    /// Toute clé écrite dans une fenêtre ou dans le code doit exister. WPF ne
    /// signale rien quand elle manque : l'étiquette affiche la clé.
    /// </summary>
    [Fact]
    public void Chaque_cle_employee_existe()
    {
        var connues = Keys(string.Empty);
        var racine = RepositoryRoot();

        List<string> manquantes = [];

        foreach (var fichier in Directory
            .EnumerateFiles(Path.Combine(racine, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var contenu = File.ReadAllText(fichier);

            foreach (Match match in UsedInXaml.Matches(contenu).Concat(UsedInCode.Matches(contenu)))
            {
                var cle = match.Groups["key"].Value;

                if (!connues.Contains(cle))
                {
                    manquantes.Add($"{Path.GetFileName(fichier)} : {cle}");
                }
            }
        }

        Assert.Equal([], manquantes.Distinct().Order());
    }

    /// <summary>
    /// Éprouve la chaîne entière : ressources embarquées, satellites produits,
    /// et le repli quand la culture n'est pas servie. Sans cette épreuve, un
    /// « SatelliteResourceLanguages » trop étroit passerait inaperçu.
    /// </summary>
    [Theory]
    [InlineData("en", "Quit")]
    [InlineData("fr", "Quitter")]
    [InlineData("fr-BE", "Quitter")]
    [InlineData("es", "Salir")]
    [InlineData("es-MX", "Salir")]
    [InlineData("de-DE", "Quit")]
    public void Le_texte_se_lit_dans_la_langue_demandee(string culture, string attendu)
        => Assert.Equal(attendu, Strings.GetIn("Quit", CultureInfo.GetCultureInfo(culture)));

    /// <summary>
    /// Le chemin de production : à l'écran, aucune culture n'est passée, c'est
    /// celle du fil qui décide. C'est ce que pose <c>App.ApplyLanguageAsync</c>.
    /// </summary>
    [Theory]
    [InlineData("fr-FR", "Quitter")]
    [InlineData("es-ES", "Salir")]
    [InlineData("de-DE", "Quit")]
    public void Le_texte_suit_la_langue_du_fil(string culture, string attendu)
    {
        var avant = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            Assert.Equal(attendu, Strings.Get("Quit"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = avant;
        }
    }

    [Fact]
    public void Une_cle_inconnue_se_rend_telle_quelle()
        => Assert.Equal("PasUneCle", Strings.GetIn("PasUneCle", CultureInfo.InvariantCulture));

    private static HashSet<string> Keys(string langue)
        => [.. Entries(langue).Select(e => e.Key)];

    private static IEnumerable<KeyValuePair<string, string>> Entries(string langue)
    {
        var suffixe = string.IsNullOrEmpty(langue) ? string.Empty : $".{langue}";
        var chemin = Path.Combine(
            RepositoryRoot(), "src", "DtHub.Core", "Localization", $"Strings{suffixe}.resx");

        return XDocument.Load(chemin)
            .Root!
            .Elements("data")
            .Select(d => new KeyValuePair<string, string>(
                d.Attribute("name")!.Value,
                d.Element("value")?.Value ?? string.Empty));
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

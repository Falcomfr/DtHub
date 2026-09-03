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
public sealed partial class StringsResourceTests
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
    /// <summary>
    /// Les trous de format doivent concorder d'une langue à l'autre. Un
    /// « {1} » de trop dans une traduction lève une FormatException en pleine
    /// interface, et rien ne l'attrapait : cinquante et une clés portent des
    /// trous, cinquante-cinq appels à Strings.Format les remplissent, et aucune
    /// épreuve n'appelait Format une seule fois.
    /// </summary>
    [Fact]
    public void Les_trous_de_format_concordent_dans_les_trois_langues()
    {
        var reference = Entries("").ToDictionary(e => e.Key, e => Trous(e.Value), StringComparer.Ordinal);

        List<string> ecarts = [];

        foreach (var langue in new[] { "fr", "es" })
        {
            foreach (var entry in Entries(langue))
            {
                if (reference.TryGetValue(entry.Key, out var attendus)
                    && !attendus.SetEquals(Trous(entry.Value)))
                {
                    ecarts.Add($"{entry.Key} ({langue})");
                }
            }
        }

        Assert.Equal([], ecarts.Order());
    }

    /// <summary>
    /// Et le remplissage lui-même ne doit pas lever, dans aucune des trois
    /// langues. Le contrôle précédent compare des ensembles ; celui-ci exécute
    /// vraiment le formatage, avec assez d'arguments pour tous les trous.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Chaque_texte_a_trous_se_remplit_sans_lever(string langue)
    {
        var culture = CultureInfo.GetCultureInfo(langue);

        foreach (var entry in Entries(langue == "en" ? "" : langue))
        {
            var trous = Trous(entry.Value);

            if (trous.Count == 0)
            {
                continue;
            }

            var arguments = Enumerable.Range(0, trous.Max() + 1)
                .Select(object (i) => $"valeur{i}")
                .ToArray();

            var texte = string.Format(culture, Strings.GetIn(entry.Key, culture), arguments);

            Assert.False(string.IsNullOrWhiteSpace(texte));
        }
    }

    /// <summary>Les numéros de trou d'un texte, « {0} » et « {1:0.0} » compris.</summary>
    private static HashSet<int> Trous(string texte) =>
        [.. FormatHole().Matches(texte).Select(m => int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture))];

    [GeneratedRegex(@"\{(?<n>\d+)(?::[^}]*)?\}")]
    private static partial Regex FormatHole();

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

    /// <summary>
    /// Aucune fenêtre ne doit porter de texte en dur. Le contrôle est
    /// mécanique parce que l'oubli l'est aussi : on ajoute un bouton, on tape
    /// son libellé, et l'application redevient française dans un coin.
    /// </summary>
    [Fact]
    public void Aucune_fenetre_ne_porte_de_texte_en_dur()
    {
        // Le nom du produit, le signe du pourcentage et les glyphes ne se
        // traduisent pas : fermeture, chevrons du fil d'Ariane, et la flèche de
        // retour des écrans Android dessinés dans l'aide.
        HashSet<string> admis =
        [
            "DT Hub", "%", "\u2715",
            "\u2039", "\u203a", "&#x2039;", "&#x203A;",
            "\u2190", "&#x2190;",
        ];

        var attributs = new Regex(
            @"(?<!\w)(?:Content|Text|Title|ToolTip|Header)=""(?<value>[^""{][^""]*)""");

        List<string> durs = [];

        foreach (var fichier in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src"), "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match match in attributs.Matches(File.ReadAllText(fichier)))
            {
                var valeur = match.Groups["value"].Value.Trim();

                if (!admis.Contains(valeur))
                {
                    durs.Add($"{Path.GetFileName(fichier)} : {valeur}");
                }
            }
        }

        Assert.Equal([], durs.Order());
    }

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

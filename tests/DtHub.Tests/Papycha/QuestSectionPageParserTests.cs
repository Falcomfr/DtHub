using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestSectionPageParserTests
{
    /// <summary>Forme du tableau relevée sur la page « Quêtes » du site.</summary>
    private const string Index = """
        <p>Un texte d'introduction et une image.</p>
        <figure class="wp-block-table"><table><tbody>
        <tr>
          <td><a href="https://papycha.fr/quetes/quetes-principales/"><strong>Quêtes principales</strong></a></td>
          <td><a href="https://papycha.fr/quetes-dalignement/">Quêtes d&#8217;Alignement</a></td>
        </tr>
        <tr>
          <td><a href="https://papycha.fr/?page_id=391">Quêtes de Frigost</a></td>
          <td><a href="https://papycha.fr/quetes-repetables/">Quêtes répétables</a></td>
        </tr>
        </tbody></table></figure>
        """;

    [Fact]
    public void Le_tableau_donne_les_rubriques_dans_l_ordre_du_site()
    {
        var sections = QuestSectionPageParser.ParseIndex(Index);

        Assert.Equal(
            ["Quêtes principales", "Quêtes d’Alignement", "Quêtes de Frigost", "Quêtes répétables"],
            sections.Select(s => s.Name));
    }

    [Fact]
    public void Une_adresse_par_identifiant_est_gardee_telle_quelle()
    {
        // Le site emploie les deux formes dans son propre tableau : le lien vers
        // Frigost passe par un identifiant, les autres par un chemin.
        var sections = QuestSectionPageParser.ParseIndex(Index);

        Assert.Contains(sections, s => s.Url == "https://papycha.fr/?page_id=391");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>une page sans tableau</p>")]
    public void Une_page_sans_tableau_ne_donne_aucune_rubrique(string? html)
    {
        // Le catalogue continue alors sur les seules catégories : une question
        // de rangement ne doit jamais faire échouer l'indexation.
        Assert.Empty(QuestSectionPageParser.ParseIndex(html));
    }

    [Fact]
    public void Une_page_de_rubrique_donne_les_quetes_qu_elle_enumere()
    {
        const string content = """
            <p>Voici les quêtes de la zone.</p>
            <ul>
              <li><a href="https://papycha.fr/quete-le-dragon-dastrub/">Le dragon d’Astrub</a></li>
              <li><a href="https://papycha.fr/quete-un-nouveau-dofus/#etape-2">Un nouveau Dofus ?</a></li>
              <li><a href="https://www.dofus-touch.com/">Le site officiel</a></li>
            </ul>
            """;

        var links = QuestSectionPageParser.ParseQuestLinks(content);

        // Le lien extérieur est écarté, et l'ancre ne fait pas de la seconde
        // quête une page différente.
        Assert.Equal(
            [
                "https://papycha.fr/quete-le-dragon-dastrub",
                "https://papycha.fr/quete-un-nouveau-dofus",
            ],
            links);
    }

    [Fact]
    public void Un_meme_lien_cite_deux_fois_n_est_compte_qu_une()
    {
        const string content = """
            <a href="https://papycha.fr/quete-le-dragon-dastrub/">Le dragon d’Astrub</a>
            <a href="https://papycha.fr/quete-le-dragon-dastrub">encore lui</a>
            """;

        Assert.Single(QuestSectionPageParser.ParseQuestLinks(content));
    }

    [Theory]
    [InlineData("https://papycha.fr/quete-x/", "https://papycha.fr/quete-x")]
    [InlineData("https://papycha.fr/quete-x#etape", "https://papycha.fr/quete-x")]
    [InlineData(null, "")]
    public void Deux_ecritures_d_une_meme_adresse_se_valent(string? url, string expected)
    {
        Assert.Equal(expected, QuestSectionPageParser.Key(url));
    }
}

using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestSectionPageParserTests
{
    /// <summary>
    /// Table layout captured on the site's "Quêtes" (Quests) page.
    /// </summary>
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
        // The site uses both forms in its own table: the link to
        // Frigost goes through an id, the others through a path.
        var sections = QuestSectionPageParser.ParseIndex(Index);

        Assert.Contains(sections, s => s.Url == "https://papycha.fr/?page_id=391");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>une page sans tableau</p>")]
    public void Une_page_sans_tableau_ne_donne_aucune_rubrique(string? html)
    {
        // The catalog then carries on with only the categories: a
        // matter of organization must never make indexing fail.
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

        // The external link is discarded, and the anchor does not
        // turn the second quest into a different page.
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

    /// <summary>
    /// Layout captured on the "Quêtes d'Astrub" page.
    /// </summary>
    private const string Zone = """
        <div class="wp-block-image"><figure><img src="x.png" /></figure></div>
        <p class="wp-block-paragraph"><strong>[Succès] Quand on arrive en ville :</strong></p>
        <ul class="wp-block-list">
        <li><a href="https://papycha.fr/quete-rencontre-du-ratieme-type/">Rencontre du ratième type</a></li>
        <li><a href="https://papycha.fr/quete-lastrub-den-bas/">L&rsquo;Astrub d&rsquo;en bas</a></li>
        </ul>
        <p class="wp-block-paragraph"><strong>[Succès] Un piou, c&rsquo;est tout ! :</strong></p>
        <ul class="wp-block-list">
        <li><a href="https://papycha.fr/quete-origine-inpiounnue/">Origine Inpiounnue</a></li>
        </ul>
        <p class="wp-block-paragraph"><strong>Divers :</strong></p>
        <ul class="wp-block-list">
        <li><a href="https://papycha.fr/quete-un-truc-a-part/">Un truc à part</a></li>
        </ul>
        """;

    [Fact]
    public void Les_intertitres_d_une_page_de_zone_donnent_ses_succes()
    {
        var groups = QuestSectionPageParser.ParseGroups(Zone);

        Assert.Equal(3, groups.Count);
        Assert.Equal("Quand on arrive en ville", groups[0].Name);
        Assert.Equal(2, groups[0].QuestUrls.Count);
        Assert.Equal("Un piou, c’est tout !", groups[1].Name);
    }

    [Fact]
    public void Un_intertitre_qui_n_annonce_pas_un_succes_ne_pretend_pas_en_etre_un()
    {
        // A page also says "Divers" or "Quêtes des Calanques
        // d'Astrub": treating them as achievements would invent
        // some.
        var groups = QuestSectionPageParser.ParseGroups(Zone);

        Assert.True(groups[0].IsSuccess);
        Assert.True(groups[1].IsSuccess);

        Assert.False(groups[2].IsSuccess);
        Assert.Equal("Divers", groups[2].Name);
    }

    [Fact]
    public void Un_intertitre_qui_ne_coiffe_aucune_quete_est_ecarte()
    {
        const string content = """
            <p><strong>[Succès] Un succès sans lien :</strong></p>
            <p>Rien ici.</p>
            """;

        Assert.Empty(QuestSectionPageParser.ParseGroups(content));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>une page sans intertitre</p>")]
    public void Une_page_sans_intertitre_ne_donne_aucun_groupe(string? content)
    {
        Assert.Empty(QuestSectionPageParser.ParseGroups(content));
    }
}

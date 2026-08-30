using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestMenuParserTests
{
    /// <summary>Fragment du menu du site, dans son ordre réel.</summary>
    private const string Menu = """
        <a href="https://papycha.fr/quetes/">Quêtes</a>
        <a href="https://papycha.fr/quetes/quetes-principales/">Quêtes Principales</a>
        <a href="https://papycha.fr/quetes/quetes-repetables/">Quêtes répétables</a>
        <a href="https://papycha.fr/quetes/quetes-dalbuera-et-dastrub/quetes-dastrub/">Quêtes d&rsquo;Astrub</a>
        <a href="https://papycha.fr/quetes-de-frigost/">Quêtes de Frigost</a>
        <a href="https://papycha.fr/chemins/">Chemins</a>
        <a href="https://papycha.fr/guides/">Guides</a>
        """;

    [Fact]
    public void L_ordre_du_menu_est_repris_tel_quel()
    {
        var ordre = QuestMenuParser.ParseOrder(Menu);

        // La racine « Quêtes » n'est pas une rubrique : elle ne range rien
        // qu'une autre ne range déjà.
        Assert.Equal("quetes principales", ordre[0]);
        Assert.Equal("quetes repetables", ordre[1]);
        Assert.Equal("quetes d astrub", ordre[2]);
        Assert.Equal("quetes de frigost", ordre[3]);
    }

    [Fact]
    public void La_lecture_s_arrete_quand_le_menu_quitte_les_quetes()
    {
        // Le menu enchaîne les chemins et les guides, qui ne rangent aucune
        // quête : les garder fausserait le classement.
        var ordre = QuestMenuParser.ParseOrder(Menu);

        Assert.DoesNotContain("chemins", ordre);
        Assert.DoesNotContain("guides", ordre);
    }

    [Fact]
    public void Une_rubrique_est_reconnue_dans_l_intitule_qui_la_contient()
    {
        // Le menu dit « Quêtes d'Astrub » là où la catégorie dit « Astrub » :
        // une égalité ne se produirait jamais.
        var ordre = QuestMenuParser.ParseOrder(Menu);

        Assert.Equal(2, QuestMenuParser.RankOf(ordre, QuestSearch.Normalize("Astrub")));

        // « Île de Frigost » face à « Quêtes de Frigost » : c'est le nom propre
        // qui rapproche, pas la phrase entière.
        Assert.Equal(3, QuestMenuParser.RankOf(ordre, QuestSearch.Normalize("Île de Frigost")));
    }

    [Fact]
    public void Une_rubrique_absente_du_menu_passe_a_la_fin()
    {
        var ordre = QuestMenuParser.ParseOrder(Menu);

        Assert.Equal(int.MaxValue, QuestMenuParser.RankOf(ordre, "dedale"));
        Assert.Equal(int.MaxValue, QuestMenuParser.RankOf(ordre, string.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>une page sans menu</p>")]
    public void Une_page_sans_menu_ne_donne_aucun_ordre(string? html)
    {
        // Sans ordre, les rubriques se rangent par nombre de quêtes : c'est une
        // question de présentation, jamais de quoi faire échouer l'indexation.
        Assert.Empty(QuestMenuParser.ParseOrder(html));
    }
}

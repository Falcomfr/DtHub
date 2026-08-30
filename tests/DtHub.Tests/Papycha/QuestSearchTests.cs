using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestSearchTests
{
    [Theory]
    [InlineData("Le dragon d'Astrub", "le dragon d astrub")]
    [InlineData("Complètement givré", "completement givre")]
    [InlineData("Dans les pas du Chevalier de l’Automne", "dans les pas du chevalier de l automne")]
    [InlineData("Un nouveau Dofus ?", "un nouveau dofus")]
    [InlineData("  Étape   1/61  ", "etape 1 61")]
    public void Un_titre_se_reduit_a_une_forme_comparable(string title, string expected)
    {
        // Les titres du site portent accents et apostrophes courbes ; personne
        // ne les tape ainsi.
        Assert.Equal(expected, QuestSearch.Normalize(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!?,.")]
    public void Une_saisie_sans_lettre_ne_filtre_rien(string? query)
    {
        Assert.Empty(QuestSearch.Terms(query));
        Assert.True(QuestSearch.Matches("le dragon d astrub", QuestSearch.Terms(query)));
    }

    [Fact]
    public void Les_mots_se_cherchent_dans_n_importe_quel_ordre()
    {
        var key = QuestSearch.Normalize("Le dragon d'Astrub");

        Assert.True(QuestSearch.Matches(key, QuestSearch.Terms("dragon astrub")));
        Assert.True(QuestSearch.Matches(key, QuestSearch.Terms("astrub dragon")));
        Assert.False(QuestSearch.Matches(key, QuestSearch.Terms("dragon bouftou")));
    }

    [Fact]
    public void Un_titre_qui_commence_par_la_recherche_passe_devant()
    {
        // On lit de gauche à droite : le début d'un titre pèse plus que son
        // milieu.
        QuestSummary Quete(string title) => new()
        {
            Title = title,
            SearchKey = QuestSearch.Normalize(title),
        };

        var resultats = QuestSearch.Filter(
            [Quete("Un nouveau Dofus ?"), Quete("Dofus Cawotte")],
            "dofus");

        Assert.Equal("Dofus Cawotte", resultats[0].Title);
    }

    [Fact]
    public void Le_nombre_de_resultats_est_borne()
    {
        var quetes = Enumerable.Range(0, 200).Select(i => new QuestSummary
        {
            Title = $"Quête {i}",
            SearchKey = QuestSearch.Normalize($"Quête {i}"),
        });

        Assert.Equal(10, QuestSearch.Filter(quetes, "quete", limit: 10).Count);
    }
}

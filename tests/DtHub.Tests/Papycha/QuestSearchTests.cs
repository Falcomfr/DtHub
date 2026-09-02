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

    [Fact]
    public void Une_quete_ne_se_trouve_plus_par_le_nom_de_sa_zone()
    {
        // Elle s'y trouvait, et c'est ce qui noyait la recherche : « frigost »
        // rendait cent soixante-dix-sept quêtes dont cent soixante-treize par
        // la seule rubrique. Cette intention est servie par le groupe des
        // zones, qui n'existait pas quand la rubrique a été ajoutée ici.
        var quete = new QuestSummary
        {
            Title = "Complètement givré",
            SearchKey = QuestSearch.Normalize("Complètement givré"),
        };

        Assert.Single(QuestSearch.Filter([quete], "givre"));
        Assert.Empty(QuestSearch.Filter([quete], "frigost"));
    }

    [Fact]
    public void Ce_qui_commence_par_le_mot_cherche_passe_devant()
    {
        // Les titres français commencent souvent par un article : sans ce
        // classement, « Dragon Cochon » se perdrait derrière « Le dragon
        // d'Astrub ».
        QuestSummary Quete(string title) => new()
        {
            Title = title,
            SearchKey = QuestSearch.Normalize(title),
        };

        var resultats = QuestSearch.Filter(
            [
                Quete("Le dragon d'Astrub"),
                Quete("Dragon Cochon"),
                Quete("Antre du Dragon"),
            ],
            "dragon");

        Assert.Equal(3, resultats.Count);
        Assert.Equal("Dragon Cochon", resultats[0].Title);
    }

    private static QuestSummary Quete(string titre, string succes = "") =>
        new()
        {
            Title = titre,
            SearchKey = QuestSearch.Normalize(titre),
            SuccessName = succes,
        };

    private static QuestSection Rubrique(int id, string nom) =>
        new() { Id = id, Name = nom, SearchKey = QuestSearch.Normalize(nom) };

    [Fact]
    public void La_recherche_distingue_les_zones_les_succes_et_les_quetes()
    {
        QuestSummary[] quetes =
        [
            Quete("Bienvenue à Frigost", "Les survivants de Frigost"),
            Quete("Complètement givré", "Les survivants de Frigost"),
            Quete("Le dragon d'Astrub", "Devenir une légende"),
        ];

        QuestSection[] rubriques = [Rubrique(135, "Île de Frigost"), Rubrique(18, "Astrub")];

        var trouve = QuestSearch.Search(quetes, rubriques, [], [], "frigost");

        Assert.Equal("Île de Frigost", Assert.Single(trouve.Zones).Name);
        Assert.Equal("Les survivants de Frigost", Assert.Single(trouve.Successes));

        // « Complètement givré » se déroule à Frigost mais ne le dit pas dans
        // son nom : elle relève de la zone, pas du groupe des quêtes.
        Assert.Equal("Bienvenue à Frigost", Assert.Single(trouve.Quests).Title);
    }

    [Fact]
    public void Un_succes_se_cherche_par_son_nom()
    {
        // Les succès n'étaient cherchables par aucun chemin, alors que le
        // catalogue en porte plus de cent.
        QuestSummary[] quetes =
        [
            Quete("Une quête", "Intérimaire frigostien"),
            Quete("Une autre", "Intérimaire frigostien"),
            Quete("Une troisième", "Objets trouvés"),
        ];

        var trouve = QuestSearch.Search(quetes, [], [], [], "interimaire");

        Assert.Equal("Intérimaire frigostien", Assert.Single(trouve.Successes));
    }

    [Fact]
    public void Une_zone_se_cherche_sous_le_nom_qu_on_affiche()
    {
        // La liste montre « Port de Madrestam » ; chercher ce qu'on lit doit
        // marcher, même si le catalogue nomme la rubrique autrement.
        QuestSection[] rubriques = [Rubrique(-4, "Quêtes du Port de Madrestam")];

        var trouve = QuestSearch.Search([], rubriques, [], [], "madrestam");

        Assert.Single(trouve.Zones);
    }

    [Fact]
    public void Une_recherche_vide_ne_rend_rien()
    {
        // Sans quoi la liste des zones serait remplacée par le catalogue entier
        // dès que le champ se vide.
        Assert.True(QuestSearch.Search([Quete("Une quête")], [], [], [], "  ").IsEmpty);
        Assert.True(QuestSearch.Search([Quete("Une quête")], [], [], [], null).IsEmpty);
    }
}

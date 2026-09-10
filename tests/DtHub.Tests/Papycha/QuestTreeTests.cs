using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Papycha;

/// <summary>
/// Les branches de la liste des guides. Ces règles vivaient dans la vue-modèle
/// de la fenêtre, donc dans le seul projet qu'aucune épreuve n'atteint : elles
/// décidaient de l'ordre, des intertitres et des comptes sans que rien ne les
/// vérifie. C'est ce que l'extraction rend possible.
/// </summary>
public class QuestTreeTests
{
    private static async Task<(QuestTree Tree, Dictionary<int, int> Counts)> BuildAsync(
        FakePapychaClient client)
    {
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>());
        var catalog = await service.GetAsync(null, CancellationToken.None);

        Dictionary<int, int> counts = [];

        foreach (var quest in catalog.Quests)
        {
            foreach (var section in quest.SectionIds)
            {
                counts[section] = counts.GetValueOrDefault(section) + 1;
            }
        }

        return (new QuestTree(service, counts), counts);
    }

    [Fact]
    public async Task La_racine_annonce_les_quatre_entrees_et_leurs_comptes()
    {
        var (tree, _) = await BuildAsync(new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithQuest(2, "Complètement givré", 135)
            .WithSection(18, "Astrub")
            .WithSection(135, "Île de Frigost"));

        var root = tree.Root();

        Assert.Equal(4, root.Count);
        Assert.All(root, n => Assert.Equal(QuestNodeKind.Branch, n.Kind));
        Assert.Contains("2", root[0].Detail, StringComparison.Ordinal);

        // Les identifiants des branches inventées sont négatifs, hors de portée
        // des catégories du site, qui sont positives.
        Assert.Equal(QuestTree.RootSection, root[0].Id);
        Assert.All(root.Skip(1), n => Assert.True(n.Id < 0));
    }

    [Fact]
    public async Task Le_compte_accorde_le_mot_au_nombre()
    {
        var (tree, _) = await BuildAsync(new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithSection(18, "Astrub"));

        var quetes = tree.Root()[0].Detail;

        Assert.Contains(Strings.Get("WordQuest"), quetes, StringComparison.Ordinal);
        Assert.DoesNotContain(Strings.Get("WordQuests"), quetes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Les_branches_portent_leur_zone_et_son_compte()
    {
        var (tree, _) = await BuildAsync(new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithQuest(2, "Le boufton noir", 18)
            .WithQuest(3, "Complètement givré", 135)
            .WithSection(18, "Astrub")
            .WithSection(135, "Île de Frigost"));

        var branches = tree.Branches().Where(n => n.Kind == QuestNodeKind.Branch).ToList();

        Assert.Equal(2, branches.Count);
        Assert.Contains(branches, b => b.Label.Contains("Astrub (2)", StringComparison.Ordinal));
        Assert.Contains(branches, b => b.Label.Contains("(1)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_zone_sans_quete_ne_donne_pas_de_branche()
    {
        // Le décompte fait foi, et non la liste des rubriques du site : une
        // catégorie vide ferait une branche qui ne mène nulle part.
        var (tree, _) = await BuildAsync(new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithSection(18, "Astrub")
            .WithSection(200, "Zone vide"));

        Assert.DoesNotContain(tree.Branches(), n => n.Label.Contains("Zone vide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Les_quetes_d_un_succes_arrivent_sous_leur_intertitre()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Première", 18)
            .WithQuest(2, "Deuxième", 18)
            .WithSection(18, "Astrub")
            .WithPage("Astrub", "https://papycha.fr/astrub", 1, 2)
            .WithSuccess("Devenir une légende", 1, 2);

        var (tree, _) = await BuildAsync(client);
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>());
        var catalog = await service.GetAsync(null, CancellationToken.None);

        var lignes = tree.BySuccess(catalog.Quests).ToList();

        var titre = lignes.FirstOrDefault(n => n.Kind == QuestNodeKind.Success);
        Assert.NotNull(titre);
        Assert.Contains("Devenir une légende", titre.Label, StringComparison.Ordinal);

        // Le décalage dit l'appartenance : une quête au ras de la marge n'est
        // réclamée par aucun succès.
        Assert.All(
            lignes.Where(n => n.Kind == QuestNodeKind.Quest),
            n => Assert.True(n.InSuccess));
    }

    [Fact]
    public async Task Une_quete_hors_succes_reste_au_ras_de_la_marge()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Seule au monde", 18)
            .WithSection(18, "Astrub");

        var (tree, _) = await BuildAsync(client);
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>());
        var catalog = await service.GetAsync(null, CancellationToken.None);

        var lignes = tree.BySuccess(catalog.Quests).ToList();

        Assert.DoesNotContain(lignes, n => n.Kind == QuestNodeKind.Success);
        Assert.All(lignes, n => Assert.False(n.InSuccess));
    }

    [Fact]
    public async Task Une_ligne_de_quete_porte_ses_prerequis()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithSection(18, "Astrub");

        var (tree, _) = await BuildAsync(client);
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>());
        var catalog = await service.GetAsync(null, CancellationToken.None);

        var ligne = tree.NodeOf(catalog.Quests[0]);

        Assert.Equal(QuestNodeKind.Quest, ligne.Kind);
        Assert.Equal("Le dragon d'Astrub", ligne.Label);
        Assert.NotNull(ligne.Needs);
    }

    [Fact]
    public void Les_trois_sortes_de_lieux_de_combat_sont_declarees_une_fois()
    {
        Assert.Equal(3, QuestTree.DungeonGroups.Length);
        Assert.Equal(
            QuestTree.DungeonGroups.Length,
            QuestTree.DungeonGroups.Select(g => g.Kind).Distinct().Count());
    }
    [Theory]
    [InlineData(QuestTree.DungeonSection, "Dungeons")]
    [InlineData(QuestTree.RaidSection, "Raids")]
    [InlineData(QuestTree.LairSection, "Lairs")]
    public async Task Une_branche_hors_du_site_porte_son_propre_nom(int section, string clef)
    {
        // Le repli les nommait toutes « Rubrique » : un signalement sur le
        // Minotoror portait « Rubrique › Minotoror » et ne disait donc pas où
        // regarder.
        var (tree, _) = await BuildAsync(new FakePapychaClient());

        Assert.Equal(Strings.Get(clef), tree.NameOf(section));
    }

    [Theory]
    [InlineData(QuestTree.DungeonPathSection, "Dungeons")]
    [InlineData(QuestTree.QuestPathSection, "QuestAreaCrumb")]
    public async Task Un_chemin_garde_ses_deux_rangs(int section, string cote)
    {
        var (tree, _) = await BuildAsync(new FakePapychaClient());

        Assert.Equal(
            Strings.Get(cote) + QuestTree.Separator + Strings.Get("Paths"),
            tree.NameOf(section));
    }

    [Fact]
    public async Task Une_rubrique_inconnue_reste_nommee_par_defaut()
    {
        var (tree, _) = await BuildAsync(new FakePapychaClient());

        Assert.Equal(Strings.Get("Section"), tree.NameOf(987654));
    }

}

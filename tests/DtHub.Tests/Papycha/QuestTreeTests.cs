using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The branches of the guides list. These rules used to live in the
/// window's view model, that is, in the only project no test can
/// reach: they decided the order, the subheadings, and the counts
/// without anything verifying them. This is what the extraction
/// makes possible.
/// </summary>
public class QuestTreeTests
{
    private static async Task<(QuestTree Tree, Dictionary<int, int> Counts)> BuildAsync(
        FakePapychaClient client)
    {
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>(), client);
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

        // The identifiers of invented branches are negative, out of
        // reach of the site's categories, which are positive.
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
        // The count is authoritative, not the site's list of
        // sections: an empty category would make a branch that
        // leads nowhere.
        var (tree, _) = await BuildAsync(new FakePapychaClient()
            .WithQuest(1, "Le dragon d'Astrub", 18)
            .WithSection(18, "Astrub")
            .WithSection(200, "Zone vide"));

        Assert.DoesNotContain(tree.Branches(), n => n.Label.Contains("Zone vide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_ligne_de_quete_porte_son_rythme_a_droite()
    {
        var (tree, _) = await BuildAsync(new FakePapychaClient()
            .WithQuest(1, "Adoption éphémère", 18)
            .WithSection(18, "Astrub"));

        var row = tree.NodeOf(new QuestSummary { Id = 1, Title = "Adoption éphémère", Recurrence = QuestRecurrence.Daily });

        Assert.Equal(Strings.Get("RecurrenceDaily"), row.Detail);
    }

    [Fact]
    public async Task Les_quetes_d_un_succes_arrivent_sous_leur_intertitre()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Première", 18)
            .WithQuest(2, "Deuxième", 18)
            .WithSection(18, "Astrub");

        // The achievement comes from the catalog DT Hub already knows: the
        // site's API keeps it locked, and the merge must not lose it.
        client.KnownQuests.AddRange(
        [
            new QuestSummary { Id = 1, Title = "Première", SectionIds = [18], SuccessName = "Devenir une légende" },
            new QuestSummary { Id = 2, Title = "Deuxième", SectionIds = [18], SuccessName = "Devenir une légende" },
        ]);

        var (tree, _) = await BuildAsync(client);
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>(), client);
        var catalog = await service.GetAsync(null, CancellationToken.None);

        var lignes = tree.BySuccess(catalog.Quests).ToList();

        var titre = lignes.FirstOrDefault(n => n.Kind == QuestNodeKind.Success);
        Assert.NotNull(titre);
        Assert.Contains("Devenir une légende", titre.Label, StringComparison.Ordinal);

        // The indent conveys membership: a quest flush with the
        // margin is not claimed by any achievement.
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
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>(), client);
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
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>(), client);
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
        // The fallback used to name them all "Rubrique" ("Section"): a
        // report about the Minotoror carried "Rubrique › Minotoror"
        // and therefore did not say where to look.
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

    private static QuestNode Row(int id, bool inSuccess) =>
        new(QuestNodeKind.Quest, $"q{id}", Quest: new QuestSummary { Id = id, Title = $"q{id}" }, InSuccess: inSuccess);

    private static QuestNode Heading(string name) => new(QuestNodeKind.Success, name);

    private static readonly QuestNode[] Planned =
    [
        Row(1, false),
        Heading("A"),
        Row(2, true),
        Row(3, true),
        Row(4, false),
        Heading("B"),
        Row(5, true),
    ];

    [Fact]
    public void Un_filtre_retire_des_lignes_sans_deplacer_les_autres()
    {
        var kept = QuestTree.Keep(Planned, q => q.Id != 2);

        Assert.Equal(["q1", "A", "q3", "q4", "B", "q5"], kept.Select(n => n.Label));
    }

    [Fact]
    public void Un_succes_vide_par_le_filtre_perd_son_entete()
    {
        var kept = QuestTree.Keep(Planned, q => q.Id is not (2 or 3 or 5));

        Assert.Equal(["q1", "q4"], kept.Select(n => n.Label));
    }

    [Fact]
    public void Sans_filtre_la_liste_reste_identique()
    {
        var kept = QuestTree.Keep(Planned, _ => true);

        Assert.Equal(Planned.Select(n => n.Label), kept.Select(n => n.Label));
    }
}

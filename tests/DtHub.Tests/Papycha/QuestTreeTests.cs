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

    private sealed class OneDungeonTable : ITouchIdTableSource
    {
        public TouchIdTable Load() => new() { Quests = new() { [1] = [10] }, Dungeons = new() { [5744] = new([50]) } };
    }

    [Fact]
    public async Task En_anglais_un_chemin_retrouve_son_donjon()
    {
        Strings.Speak(System.Globalization.CultureInfo.GetCultureInfo("en"));

        try
        {
            var client = new FakePapychaClient().WithQuest(1, "Q", 18).WithSection(18, "Astrub");
            client.Dungeons.Add(new DungeonSummary { Id = 5744, Kind = DungeonKind.Dungeon, Title = "Donjon Inventé", Level = 60 });
            client.Paths.Add(new PathSummary { Id = 5739, Title = "Chemin vers Donjon Inventé", Url = "https://papycha.fr/k/" });

            using var touch = new TouchNameService(new FakeDofusTouchClient(), new InMemoryDocumentStore<TouchNamesDocument>(), new OneDungeonTable());
            using var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>(), client, touch);
            await service.GetAsync(null, CancellationToken.None);
            await service.RefreshNamesAsync();

            var tree = new QuestTree(service, new Dictionary<int, int>());
            var rows = tree.DungeonNodes().Where(n => n.Kind != QuestNodeKind.Header).ToList();

            Assert.Equal("nom 50 en", service.Catalog.Dungeons.Single().Title);
            Assert.Equal([0, 5744], rows.Select(r => r.Dungeon?.Id ?? 0));
            Assert.Equal(5739, rows[0].Path?.Id);
            Assert.Equal("nom 50 en", rows[1].Dungeon?.Title);
        }
        finally
        {
            Strings.Speak(null);
        }
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
    public async Task Une_zone_recoit_les_chemins_que_son_nom_designe()
    {
        var client = new FakePapychaClient()
            .WithQuest(1, "Une quête d'Otomaï", 131)
            .WithSection(131, "Île d'Otomaï");
        client.Paths.Add(new PathSummary { Id = 3711, Title = "Aller sur l’Île d’Otomaï", Url = "https://papycha.fr/o/" });
        client.Paths.Add(new PathSummary { Id = 7247, Title = "Chouque", Url = "https://papycha.fr/c/" });

        var (tree, _) = await BuildAsync(client);

        Assert.Equal(3711, Assert.Single(tree.PathsIn(131)).Id);

        // The branch keeps only the paths no zone takes.
        Assert.Equal(7247, Assert.Single(tree.Paths(PathSide.Quests)).Id);
    }

    [Fact]
    public async Task Un_donjon_est_precede_de_la_ligne_de_son_chemin()
    {
        // A row of the list, so that the list finds it again once the path
        // is open, and named after the dungeon so that it reads as its own.
        var client = new FakePapychaClient().WithQuest(1, "Q", 18).WithSection(18, "Astrub");
        client.Dungeons.Add(new DungeonSummary { Id = 5744, Kind = DungeonKind.Dungeon, Title = "Canopée du Kimbo", Level = 60 });
        client.Dungeons.Add(new DungeonSummary { Id = 1, Kind = DungeonKind.Dungeon, Title = "Blops", Level = 60 });
        client.Paths.Add(new PathSummary { Id = 5739, Title = "Canopée du Kimbo", Url = "https://papycha.fr/k/" });

        var (tree, _) = await BuildAsync(client);
        var rows = tree.DungeonNodes().Where(n => n.Kind != QuestNodeKind.Header).ToList();

        // The path comes first: it is followed before the dungeon is entered.
        Assert.Equal([1, 0, 5744], rows.Select(r => r.Dungeon?.Id ?? 0));
        var path = rows[1];
        Assert.Equal(5739, path.Path?.Id);
        Assert.True(path.Nested);
        Assert.Equal(Strings.Format("PathTo", "Canopée du Kimbo"), path.Label);
        Assert.Equal("https://papycha.fr/k/", path.Url);
        Assert.Empty(tree.Paths(PathSide.Dungeons));

        // The dungeon knows a path heads it, which closes the bracket
        // drawn from the path down to it.
        Assert.True(rows[2].AfterPath);
        Assert.False(rows[0].AfterPath);
    }

    [Fact]
    public async Task Un_chemin_ouvert_ramene_la_liste_la_ou_il_est_affiche()
    {
        // Opening a path, then the list again, used to land on the Paths
        // branch, whatever list the path had been picked from.
        var client = new FakePapychaClient().WithQuest(1, "Q", 131).WithSection(131, "Île d'Otomaï");
        client.Dungeons.Add(new DungeonSummary { Id = 5744, Kind = DungeonKind.Dungeon, Title = "Canopée du Kimbo", Level = 60 });
        client.Paths.Add(new PathSummary { Id = 3711, Title = "Aller sur l’Île d’Otomaï" });
        client.Paths.Add(new PathSummary { Id = 7247, Title = "Chouque" });
        client.Paths.Add(new PathSummary { Id = 5739, Title = "Canopée du Kimbo" });
        client.Paths.Add(new PathSummary { Id = 1601, Title = "du donjon du Skeunk" });

        var (tree, _) = await BuildAsync(client);
        PathSummary Path(int id) => tree.Paths(PathSide.Quests).Concat(tree.Paths(PathSide.Dungeons)).Concat(tree.PathsIn(131))
            .Concat(tree.DungeonNodes().Select(n => n.Path).OfType<PathSummary>())
            .First(p => p.Id == id);

        Assert.Equal(131, tree.SectionOf(Path(3711)));
        Assert.Equal(QuestTree.QuestPathSection, tree.SectionOf(Path(7247)));
        Assert.Equal(QuestTree.DungeonSection, tree.SectionOf(Path(5739)));
        Assert.Equal(QuestTree.DungeonPathSection, tree.SectionOf(Path(1601)));
    }

    [Fact]
    public async Task Un_intertitre_de_succes_ne_porte_pas_de_niveau()
    {
        // The site gives a level for 103 quests out of 762: a range drawn from
        // so few says more about the gaps than about the achievement.
        var client = new FakePapychaClient()
            .WithQuest(1, "Première", 18).AtLevel(20)
            .WithQuest(2, "Deuxième", 18).AtLevel(60)
            .WithSection(18, "Astrub");
        client.KnownQuests.AddRange(
        [
            new QuestSummary { Id = 1, Title = "Première", SectionIds = [18], SuccessName = "Devenir une légende" },
            new QuestSummary { Id = 2, Title = "Deuxième", SectionIds = [18], SuccessName = "Devenir une légende" },
        ]);

        var (tree, _) = await BuildAsync(client);
        var service = new QuestCatalogService(client, new InMemoryDocumentStore<QuestCatalogDocument>(), client);
        var catalog = await service.GetAsync(null, CancellationToken.None);

        var titre = tree.BySuccess(catalog.Quests).First(n => n.Kind == QuestNodeKind.Success);

        Assert.Null(titre.Detail);
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

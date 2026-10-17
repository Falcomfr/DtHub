using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestCatalogMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static QuestSummary Known(int id, string title, params int[] sections) => new()
    {
        Id = id,
        Title = title,
        Url = $"https://papycha.fr/quete-{id}/",
        SearchKey = QuestSearch.Normalize(title),
        SectionIds = sections,
        SectionId = sections.FirstOrDefault(),
    };

    private static QuestSummary FromApi(int id, string title, params int[] categories) => new()
    {
        Id = id,
        Title = title,
        Url = $"https://papycha.fr/nouvelle-{id}/",
        SearchKey = QuestSearch.Normalize(title),
        Categories = categories,
    };

    private static QuestSection Section(int id, string name, int count) => new()
    {
        Id = id,
        Name = name,
        Count = count,
        SearchKey = QuestSearch.Normalize(name),
    };

    private static QuestCatalogDocument Baseline(params QuestSummary[] quests) => new()
    {
        IndexedUtc = Now.AddDays(-6),
        Quests = [.. quests],
        Sections =
        [
            Section(18, "Astrub", quests.Count(q => q.SectionIds.Contains(18))),
            Section(25, "Amakna", quests.Count(q => q.SectionIds.Contains(25))),
        ],
        SectionOrder = ["astrub", "amakna"],
        SuccessOrder = ["Se mettre au ver"],
    };

    private static QuestCatalogDocument Merge(
        QuestCatalogDocument baseline,
        IReadOnlyList<QuestSummary>? quests = null,
        IReadOnlyList<DungeonSummary>? dungeons = null,
        IReadOnlyList<PathSummary>? paths = null) =>
        QuestCatalogMerge.Apply(baseline, quests ?? [], dungeons ?? [], paths ?? [], Now.AddHours(-1), Now);

    [Fact]
    public void Une_quete_connue_prend_le_titre_l_adresse_le_niveau_et_le_depart_de_l_api_et_garde_le_reste()
    {
        var baseline = Baseline(Known(1, "Le ver", 18, 25) with
        {
            SuccessName = "Se mettre au ver",
            PlayOrder = 2,
            ChainStep = 3,
            StartPerson = "Yse Vewibad",
            Level = 20,
            StartPosition = "[1,1]",
        });

        var merged = Merge(baseline, [FromApi(1, "Le ver de terre", 25) with { Level = 30, StartPosition = "[4,-6]" }]);

        var quest = Assert.Single(merged.Quests);
        Assert.Equal("Le ver de terre", quest.Title);
        Assert.Equal("https://papycha.fr/nouvelle-1/", quest.Url);
        Assert.Equal(30, quest.Level);
        Assert.Equal("[4,-6]", quest.StartPosition);
        Assert.Equal(QuestSearch.Normalize("Le ver de terre"), quest.SearchKey);
        Assert.Equal([18, 25], quest.SectionIds);
        Assert.Equal("Se mettre au ver", quest.SuccessName);
        Assert.Equal(2, quest.PlayOrder);
        Assert.Equal(3, quest.ChainStep);
        Assert.Equal("Yse Vewibad", quest.StartPerson);
    }

    [Fact]
    public void Un_champ_vide_de_l_api_n_efface_pas_la_valeur_connue()
    {
        var baseline = Baseline(Known(1, "Le ver", 18) with { Level = 118, StartPosition = "[4,-6]" });

        var merged = Merge(baseline, [FromApi(1, "", 18)]);

        var quest = Assert.Single(merged.Quests);
        Assert.Equal(118, quest.Level);
        Assert.Equal("[4,-6]", quest.StartPosition);
        Assert.Equal("Le ver", quest.Title);
    }

    [Fact]
    public void Les_prerequis_connus_l_emportent_et_ceux_de_l_api_comblent_un_vide()
    {
        var baseline = Baseline(
            Known(1, "Avec", 18) with { Prerequisites = ["L'essentiel est dans le Lac gelé"] },
            Known(2, "Sans", 18));

        var merged = Merge(
            baseline,
            [
                FromApi(1, "Avec", 18) with { Prerequisites = ["L’essentiel est dans le Lac gelé."] },
                FromApi(2, "Sans", 18) with { Prerequisites = ["Niveau 50", "Le réveil par la force."] },
            ]);

        Assert.Equal(["L'essentiel est dans le Lac gelé"], merged.Quests.Single(q => q.Id == 1).Prerequisites);
        Assert.Equal(["Niveau 50", "Le réveil par la force."], merged.Quests.Single(q => q.Id == 2).Prerequisites);
    }

    [Fact]
    public void Une_quete_nouvelle_rejoint_sa_rubrique_connue()
    {
        var baseline = Baseline(Known(1, "Ancienne", 25));

        var merged = Merge(baseline, [FromApi(1, "Ancienne", 25), FromApi(2, "La gardienne des Polters", 25, 740, 869)]);

        var added = merged.Quests.Single(q => q.Id == 2);
        Assert.Equal([25], added.SectionIds);
        Assert.Equal(25, added.SectionId);
        Assert.Equal(2, merged.Sections.Single(s => s.Id == 25).Count);
    }

    [Fact]
    public void Une_quete_nouvelle_rejoint_la_rubrique_de_sa_zone_quand_sa_rubrique_est_inconnue()
    {
        var merged = Merge(Baseline(Known(1, "Ancienne", 25)), [FromApi(2, "Nouvelle", 724, 18)]);

        Assert.Equal([18], merged.Quests.Single(q => q.Id == 2).SectionIds);
    }

    [Fact]
    public void La_rubrique_racine_ne_range_jamais_une_quete_nouvelle()
    {
        var merged = Merge(Baseline(Known(1, "Ancienne", 25)), [FromApi(2, "Nouvelle", 7, 25)]);

        Assert.Equal([25], merged.Quests.Single(q => q.Id == 2).SectionIds);
    }

    [Fact]
    public void Une_quete_nouvelle_sans_rubrique_connue_va_dans_autres_quetes_creee_au_besoin()
    {
        var merged = Merge(Baseline(Known(1, "Ancienne", 25)), [FromApi(2, "Les Bellaphones", 2589, 2635)]);

        var added = merged.Quests.Single(q => q.Id == 2);
        Assert.Equal([QuestCatalogMerge.OtherSectionId], added.SectionIds);
        Assert.Equal(QuestCatalogMerge.OtherSectionId, added.SectionId);
        var others = merged.Sections.Single(s => s.Id == QuestCatalogMerge.OtherSectionId);
        Assert.Equal("Autres quêtes", others.Name);
        Assert.Equal(1, others.Count);
    }

    [Fact]
    public void Une_quete_nouvelle_n_est_rangee_que_sous_sa_rubrique_la_plus_petite()
    {
        var baseline = Baseline(Known(1, "A", 18), Known(2, "B", 18), Known(3, "C", 25));

        var merged = Merge(baseline, [FromApi(4, "Nouvelle", 18, 25)]);

        var added = merged.Quests.Single(q => q.Id == 4);
        Assert.Equal([25], added.SectionIds);
        Assert.Equal(25, added.SectionId);
        Assert.Equal(2, merged.Sections.Single(s => s.Id == 18).Count);
    }

    [Fact]
    public void Une_quete_absente_de_l_api_est_gardee()
    {
        var baseline = Baseline(
            Known(1, "Dévotion à Crâ", 25) with { SuccessName = "Dévotion" },
            Known(2, "Gardée aussi", 18));

        var merged = Merge(baseline, [FromApi(2, "Gardée aussi", 18)]);

        Assert.Equal(2, merged.Quests.Count);
        Assert.Equal("Dévotion", merged.Quests.Single(q => q.Id == 1).SuccessName);
        Assert.Equal(1, merged.Sections.Single(s => s.Id == 25).Count);
    }

    [Fact]
    public void Un_donjon_est_rafraichi_ajoute_et_jamais_retire()
    {
        var baseline = Baseline(Known(1, "A", 18));
        baseline.Dungeons =
        [
            new DungeonSummary { Id = 10, Kind = DungeonKind.Dungeon, Title = "Abraknydes", Level = 90, Key = "Clef des Abraknydes", SoulStone = "Moyenne" },
            new DungeonSummary { Id = 11, Kind = DungeonKind.Raid, Title = "Dark Vlad", Level = 200 },
        ];

        var merged = Merge(
            baseline,
            dungeons:
            [
                new DungeonSummary { Id = 10, Kind = DungeonKind.Dungeon, Title = "Abraknydes", Level = 100, Position = "[-9,-14]" },
                new DungeonSummary { Id = 12, Kind = DungeonKind.Lair, Title = "Arakne", Level = 10 },
            ]);

        Assert.Equal([10, 11, 12], merged.Dungeons.Select(d => d.Id));
        var refreshed = merged.Dungeons.Single(d => d.Id == 10);
        Assert.Equal(100, refreshed.Level);
        Assert.Equal("[-9,-14]", refreshed.Position);
        Assert.Equal("Clef des Abraknydes", refreshed.Key);
        Assert.Equal("Moyenne", refreshed.SoulStone);
        Assert.Equal(QuestSearch.Normalize("Arakne"), merged.Dungeons.Single(d => d.Id == 12).SearchKey);
    }

    [Fact]
    public void Un_chemin_nouveau_prend_son_cote_d_apres_les_donjons()
    {
        var baseline = Baseline(Known(1, "A", 18));
        baseline.Dungeons = [new DungeonSummary { Id = 10, Kind = DungeonKind.Dungeon, Title = "Canopée du Kimbo" }];

        var merged = Merge(
            baseline,
            paths:
            [
                new PathSummary { Id = 20, Title = "Aller à la Canopée du Kimbo", Url = "https://papycha.fr/c/" },
                new PathSummary { Id = 21, Title = "Aller sur l'Île d'Otomaï", Url = "https://papycha.fr/o/" },
            ]);

        Assert.Equal(PathSide.Dungeons, merged.Paths.Single(p => p.Id == 20).Side);
        Assert.Equal(PathSide.Quests, merged.Paths.Single(p => p.Id == 21).Side);
    }

    [Fact]
    public void Un_chemin_connu_garde_son_cote_et_prend_son_titre()
    {
        var baseline = Baseline(Known(1, "A", 18));
        baseline.Paths = [new PathSummary { Id = 20, Title = "Ancien", Side = PathSide.Dungeons }];

        var merged = Merge(baseline, paths: [new PathSummary { Id = 20, Title = "Nouveau titre", Url = "https://papycha.fr/n/" }]);

        var path = Assert.Single(merged.Paths);
        Assert.Equal(PathSide.Dungeons, path.Side);
        Assert.Equal("Nouveau titre", path.Title);
        Assert.Equal("https://papycha.fr/n/", path.Url);
        Assert.Equal(QuestSearch.Normalize("Nouveau titre"), path.SearchKey);
    }

    [Fact]
    public void La_fusion_date_le_catalogue_et_garde_les_ordres()
    {
        var merged = Merge(Baseline(Known(1, "A", 18)));

        Assert.Equal(Now, merged.IndexedUtc);
        Assert.Equal(Now.AddHours(-1), merged.SiteModifiedUtc);
        Assert.Equal(["astrub", "amakna"], merged.SectionOrder);
        Assert.Equal(["Se mettre au ver"], merged.SuccessOrder);
    }

    [Fact]
    public void La_fusion_ne_modifie_pas_le_catalogue_de_depart()
    {
        var baseline = Baseline(Known(1, "Ancienne", 25));

        _ = Merge(baseline, [FromApi(1, "Renommée", 25), FromApi(2, "Nouvelle", 999)]);

        var quest = Assert.Single(baseline.Quests);
        Assert.Equal("Ancienne", quest.Title);
        Assert.Equal(2, baseline.Sections.Count);
        Assert.Null(baseline.SiteModifiedUtc);
    }
}

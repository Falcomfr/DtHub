using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Papycha;

namespace DtHub.App.ViewModels;

/// <summary>
/// Ce que la fenêtre de quêtes affiche : la recherche, ses résultats, et la
/// quête ouverte.
/// </summary>
public sealed partial class QuestViewModel : ObservableObject
{
    private readonly QuestCatalogService _catalog;
    private readonly IDialogService _dialogs;

    public QuestViewModel(QuestCatalogService catalog, IDialogService dialogs)
    {
        _catalog = catalog;
        _dialogs = dialogs;
    }

    /// <summary>
    /// Ce que la liste déroulante montre à cet instant : les branches, les
    /// quêtes d'une rubrique, ou les résultats d'une recherche.
    /// </summary>
    public ObservableCollection<QuestNode> Nodes { get; } = [];

    /// <summary>Nombre de quêtes du catalogue rangées dans chaque rubrique.</summary>
    private readonly Dictionary<int, int> _sectionCounts = [];

    /// <summary>Rubrique ouverte, ou zéro à la racine.</summary>
    private int _section;

    /// <summary>La quête affichée, quand il y en a une.</summary>
    private QuestSummary? _current;

    /// <summary>Le donjon affiché, quand c'en est un.</summary>
    private DungeonSummary? _currentDungeon;

    /// <summary>Le chemin affiché, quand c'en est un.</summary>
    private PathSummary? _currentPath;

    /// <summary>
    /// Les quêtes quittées en suivant un lien, la dernière au sommet.
    ///
    /// Seuls les liens comptent : ceux du guide et ceux des prérequis, qui
    /// mènent ailleurs sans qu'on l'ait cherché et dont rien ne ramenait. Une
    /// voisine choisie au pied ou une quête prise dans la liste n'y entrent
    /// pas : on sait d'où l'on vient quand c'est soi qui a désigné où aller.
    /// </summary>
    private readonly Stack<QuestSummary> _visited = new();

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _questTitle = string.Empty;

    [ObservableProperty]
    private string _chainText = string.Empty;

    /// <summary>
    /// Place de la quête dans sa chaîne de prérequis, « 6 / 7 ». Affichée au
    /// pied, entre la précédente et la suivante : c'est de cette chaîne qu'elle
    /// parle, et non du succès.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    private string _chainStep = string.Empty;

    /// <summary>Vrai quand une page est ouverte dans la vue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsQuestChrome))]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    [NotifyPropertyChangedFor(nameof(CanReport))]
    private bool _hasQuest;

    /// <summary>Ce qu'on lit tant qu'aucune quête n'est ouverte.</summary>
    [ObservableProperty]
    private string _placeholder = "Cherchez un guide, ou dépliez la liste.";

    /// <summary>
    /// Ligne à mettre en évidence quand le panneau s'ouvre : celle de la quête
    /// affichée. La sélection n'était posée qu'à la flèche du bas depuis la
    /// recherche, si bien que rouvrir la liste surlignait une quête qu'on avait
    /// quittée depuis longtemps.
    /// </summary>
    [ObservableProperty]
    private QuestNode? _selectedNode;

    /// <summary>Vrai quand la liste déroulante est ouverte.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsQuestChrome))]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    [NotifyPropertyChangedFor(nameof(ShowsLoader))]
    [NotifyPropertyChangedFor(nameof(CanReport))]
    private bool _isListOpen;

    /// <summary>
    /// Vrai le temps qu'une page de guide arrive.
    ///
    /// Le bandeau annonce la nouvelle quête dès le clic, mais la vue montre
    /// encore l'ancien guide pendant une seconde ou deux : on croyait que le
    /// clic n'avait rien fait, ou pire, on lisait la mauvaise page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    [NotifyPropertyChangedFor(nameof(ShowsLoader))]
    [NotifyPropertyChangedFor(nameof(ShowsSteps))]
    private bool _isLoadingPage;

    /// <summary>
    /// Vrai quand la fenêtre est à l'écran.
    ///
    /// Il ne sert qu'à retirer la vue web quand la fenêtre se masque, ce qui est
    /// la condition pour endormir le moteur de rendu : il refuse de dormir tant
    /// qu'il se croit visible, et le dit par une erreur d'état.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    private bool _isWindowVisible = true;

    /// <summary>Vrai pendant l'indexation, pour montrer que ça travaille.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Vrai quand le bandeau d'étape doit se voir.
    ///
    /// Il s'efface tant que la liste est ouverte : elle prend alors toute la
    /// hauteur, et on ne consulte pas une étape et une liste en même temps.
    /// </summary>
    public bool ShowsQuestChrome => HasQuest && !IsListOpen;

    /// <summary>
    /// Vrai quand le pied de succès a quelque chose à dire.
    ///
    /// Un donjon, un raid, une tanière et un chemin n'appartiennent à aucune
    /// suite : ils n'ont ni quête avant, ni quête après, ni rang dans un succès,
    /// et le pied ne montrait pour eux qu'un filet et une bande vide.
    /// </summary>
    public bool ShowsChain =>
        ShowsQuestChrome
        && (PreviousQuest is not null || NextQuest is not null || ChainStep.Length > 0);

    /// <summary>
    /// Vrai quand la vue web doit se voir. Elle est retirée pendant un
    /// chargement, et non recouverte : une fenêtre native se dessine au-dessus
    /// de tout élément WPF du même châssis, et un voile posé dessus resterait
    /// invisible. C'est du reste ce que fait déjà la liste déroulante.
    /// </summary>
    public bool ShowsPage => !IsListOpen && !IsLoadingPage && IsWindowVisible;

    /// <summary>Vrai quand la place de la vue revient à l'indicateur d'attente.</summary>
    public bool ShowsLoader => IsLoadingPage && !IsListOpen;

    /// <summary>
    /// Vrai quand la ligne d'étape doit se voir.
    ///
    /// Elle tient sa place pendant le chargement, où l'on ne connaît pas encore
    /// les étapes : sans cela le bandeau perdait une ligne puis la reprenait, et
    /// sautait à chaque changement de quête. Ce qu'elle montre alors est le
    /// départ, qui vient des métadonnées et n'attend pas la page.
    /// </summary>
    public bool ShowsSteps => HasSteps || IsLoadingPage;

    /// <summary>Où l'on se trouve dans l'arbre, affiché au-dessus de la liste.</summary>
    [ObservableProperty]
    private string _breadcrumb = string.Empty;

    /// <summary>Adresse de la page ouverte, pour la rouvrir dans le navigateur.</summary>
    public string? CurrentUrl { get; private set; }

    /// <summary>
    /// Prépare le catalogue. La fenêtre reste utilisable pendant l'indexation :
    /// elle dure quelques secondes la première fois, et rien ensuite.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var before = QuestTally.Of(_catalog.Catalog);

        IsBusy = true;

        var progress = new Progress<QuestIndexingProgress>(
            p => StatusText = p.Total > 0
                ? $"Indexation {p.Loaded} / {p.Total}"
                : "Indexation…");

        var catalog = await _catalog.GetAsync(progress, cancellationToken).ConfigureAwait(true);

        Settle(catalog, before);
        CountSections();
        ShowRoot();
    }

    /// <summary>
    /// Range ce qui suit une lecture : la chaîne et l'état affiché.
    /// </summary>
    private void Settle(QuestCatalogDocument catalog, QuestTally before)
    {
        _chain = new QuestChainIndex(catalog.Quests);

        IsBusy = false;

        // Ce que la relecture a rapporté, quand il y avait quelque chose avant
        // à quoi le comparer. La première indexation se tait : annoncer « sept
        // cent quatre-vingt-deux quêtes de plus » n'apprendrait rien.
        //
        // C'est dit sans qu'on l'ait demandé, parce que personne ne demande une
        // relecture : la sentinelle décide, et l'on veut savoir ce qu'elle a
        // trouvé.
        StatusText = catalog.Quests.Count == 0
            ? "Aucune quête : le site n'a pas répondu."
            : _catalog.LastFailure is not null
                ? "Le site n'a pas répondu ; liste en cache."
                : QuestTally.Of(catalog).Since(before);
    }

    /// <summary>
    /// Compte les quêtes par rubrique à partir du catalogue, et non des
    /// nombres du site : celui-ci compte aussi ce qui n'est pas une quête, et
    /// proposerait des rubriques qui s'ouvriraient sur rien.
    ///
    /// Sur toutes les rubriques auxquelles la quête appartient, comme la liste
    /// les montre : le site range « Le dragon d'Astrub » dans ses quêtes
    /// principales comme dans celles d'Astrub.
    /// </summary>
    private void CountSections()
    {
        _sectionCounts.Clear();

        foreach (var quest in _catalog.Catalog.Quests)
        {
            foreach (var section in quest.SectionIds)
            {
                _sectionCounts[section] = _sectionCounts.GetValueOrDefault(section) + 1;
            }
        }
    }

    partial void OnQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Effacer la recherche ramène là où l'on était, plutôt qu'à la
            // racine : on efface souvent pour corriger une faute de frappe.
            ShowSection(_section);
            return;
        }

        ShowSearch(value);

        // Chercher sans voir les résultats n'a pas de sens : jusqu'ici taper du
        // texte reconstruisait la liste sans la déployer.
        IsListOpen = true;
    }

    /// <summary>Ouvre la liste sur ce qui était affiché.</summary>
    public void OpenList()
    {
        if (Nodes.Count == 0)
        {
            ShowRoot();
        }

        // La liste rouvre sur la rubrique de la quête affichée, sauf si elle y
        // est déjà : elle garde alors ses succès dépliés et ce qu'on y avait
        // déroulé, on y retrouve seulement où l'on en est.
        //
        // Sans cela, on rouvrait sur « Quêtes / Donjons » ou sur les résultats
        // d'une recherche alors qu'un guide était affiché, et il fallait
        // redescendre l'arbre pour retrouver les voisines de ce qu'on lisait.
        // Un donjon n'appartient à aucune rubrique du site : sa branche est la
        // sienne.
        var section = (_current is { } lue ? SectionSeen(lue) : (int?)null)
            ?? (_currentDungeon is { } place ? SectionOf(place) : (int?)null)
            ?? (_currentPath is { } road
                ? road.Side == PathSide.Dungeons ? DungeonPathSection : QuestPathSection
                : (int?)null);

        if (section is { } target && (_section != target || Query.Length > 0))
        {
            Query = string.Empty;
            ShowSection(target);
        }

        SelectCurrent();

        IsListOpen = true;
    }

    /// <summary>
    /// La rubrique sous laquelle une quête se lit : celle qu'on parcourt quand
    /// elle en fait partie, sinon celle que le catalogue lui a retenue.
    ///
    /// Une quête peut appartenir à plusieurs rubriques, et le catalogue n'en
    /// garde qu'une dans <c>SectionId</c>, la moins peuplée. Rouvrir la liste
    /// depuis une quête répétable lue dans Frigost basculait donc sur « Quêtes
    /// répétables », alors qu'on venait de Frigost. Relevé sur le catalogue :
    /// 215 quêtes sur 782 appartiennent à plus d'une rubrique, dont 93 qu'une
    /// rubrique transverse emporte et 80 dans l'autre sens.
    /// </summary>
    private int SectionSeen(QuestSummary quest) =>
        quest.SectionIds.Contains(_section) ? _section : quest.SectionId;

    /// <summary>
    /// Pose la sélection sur la quête affichée, si elle est dans la liste.
    /// Ne touche à rien quand elle n'y est pas : la liste montre peut-être une
    /// autre rubrique, et la vider serait pire que de ne rien surligner.
    /// </summary>
    public void SelectCurrent()
    {
        if (string.IsNullOrEmpty(CurrentUrl))
        {
            return;
        }

        SelectedNode = Nodes.FirstOrDefault(n =>
            n.Url is { } url && string.Equals(url, CurrentUrl, StringComparison.Ordinal));
    }

    /// <summary>Le premier niveau : les grandes branches.</summary>
    public void ShowRoot()
    {
        _section = 0;
        Breadcrumb = string.Empty;
        ClearBack();

        Nodes.Clear();
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            "Quêtes",
            Nombre(_catalog.Catalog.Quests.Count),
            Id: RootSection,
            Glyph: QuestNodeGlyph.Quests));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            "Donjons",
            Combien(Fighting(DungeonKind.Dungeon).Count, "donjon", "donjons"),
            Id: DungeonSection,
            Glyph: QuestNodeGlyph.Dungeons));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            "Raids",
            Combien(Fighting(DungeonKind.Raid).Count, "raid", "raids"),
            Id: RaidSection,
            Glyph: QuestNodeGlyph.Raids));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            "Tanières",
            Combien(Fighting(DungeonKind.Lair).Count, "tanière", "tanières"),
            Id: LairSection,
            Glyph: QuestNodeGlyph.Lairs));
    }

    /// <summary>
    /// Branches qui ne viennent pas des catégories du site. Leurs identifiants
    /// sont négatifs, hors de portée de celles-ci, qui sont positives.
    /// </summary>
    private const int DungeonSection = -100;
    private const int RaidSection = -101;
    private const int LairSection = -102;
    private const int QuestPathSection = -103;
    private const int DungeonPathSection = -104;

    /// <summary>
    /// Le contenu d'une rubrique. À la racine des quêtes, ce sont les autres
    /// rubriques ; plus bas, ce sont les quêtes elles-mêmes.
    /// </summary>
    public void ShowSection(int section)
    {
        _section = section;

        if (section == 0)
        {
            ShowRoot();
            return;
        }

        Nodes.Clear();

        if (section == DungeonSection)
        {
            Breadcrumb = "Donjons";
            SetBack(target: 0);

            Nodes.Add(PathBranch(PathSide.Dungeons, DungeonPathSection));

            foreach (var node in DungeonNodes())
            {
                Nodes.Add(node);
            }

            return;
        }

        if (section is RaidSection or LairSection)
        {
            var kind = section == RaidSection ? DungeonKind.Raid : DungeonKind.Lair;

            Breadcrumb = section == RaidSection ? "Raids" : "Tanières";
            SetBack(target: 0);

            // Sans paliers : dix lignes se lisent d'un trait, et les couper
            // n'aiderait personne.
            foreach (var place in Fighting(kind).OrderBy(d => d.Level)
                         .ThenBy(d => d.Title, StringComparer.CurrentCulture))
            {
                Nodes.Add(ToNode(place));
            }

            return;
        }

        if (section is QuestPathSection or DungeonPathSection)
        {
            var side = section == DungeonPathSection ? PathSide.Dungeons : PathSide.Quests;

            Breadcrumb = side == PathSide.Dungeons ? "Donjons  ›  Chemins" : "Zone de Quêtes  ›  Chemins";
            SetBack(target: side == PathSide.Dungeons ? DungeonSection : RootSection);

            foreach (var path in Paths(side).OrderBy(p => p.Title, StringComparer.CurrentCulture))
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Quest, path.Title, Path: path, Glyph: QuestNodeGlyph.Route));
            }

            return;
        }

        if (section == RootSection)
        {
            Breadcrumb = "Zone de Quêtes";
            SetBack(target: 0);

            Nodes.Add(PathBranch(PathSide.Quests, QuestPathSection));

            foreach (var branch in Branches())
            {
                Nodes.Add(branch);
            }

            return;
        }

        Breadcrumb = $"Zone de Quêtes  ›  {NameOf(section)}";
        SetBack(target: RootSection);

        AddBySuccess(_catalog.InSection(section));
    }

    /// <summary>
    /// Rubrique où le retour ramène, quand il y a un cran au-dessus.
    ///
    /// Le retour était une ligne de la liste comme une autre : il défilait avec
    /// elle et disparaissait dès qu'on descendait dans une rubrique de soixante
    /// quêtes. Il est maintenant fixe, au-dessus de la liste.
    /// </summary>
    [ObservableProperty]
    private bool _canGoBack;

    /// <summary>Vrai quand l'historique des quêtes a de quoi revenir.</summary>
    [ObservableProperty]
    private bool _canGoBackQuest;

    private int _backTarget;

    private void SetBack(int target)
    {
        _backTarget = target;
        CanGoBack = true;
    }

    private void ClearBack() => CanGoBack = false;

    /// <summary>
    /// Revient sur la quête d'où l'on vient, et rend son adresse pour que la
    /// fenêtre la charge. Rend <c>null</c> quand l'historique est vide.
    ///
    /// Le retour ne s'empile pas lui-même : seul un lien suivi sur place le
    /// fait, sans quoi la flèche ferait la navette entre deux quêtes.
    /// </summary>
    public string? GoBackQuest()
    {
        if (_visited.Count == 0)
        {
            return null;
        }

        var quest = _visited.Pop();

        SetCurrent(quest);

        CanGoBackQuest = _visited.Count > 0;
        IsListOpen = false;

        return quest.Url;
    }

    /// <summary>Remonte d'un cran.</summary>
    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        Query = string.Empty;
        ShowSection(_backTarget);
    }

    /// <summary>
    /// Résultats d'une recherche. Elle porte sur toutes les quêtes, quelle que
    /// soit la rubrique ouverte : on cherche un nom, pas un rangement.
    /// </summary>
    private void ShowSearch(string query)
    {
        Breadcrumb = "Recherche";
        ClearBack();

        Nodes.Clear();

        var found = _catalog.SearchAll(query, limit: 60);

        if (found.Zones.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Section, "Zones", Combien(found.Zones.Count, "zone", "zones")));

            foreach (var zone in found.Zones)
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Branch,
                    $"{QuestZoneOrder.DisplayName(zone.Name)} ({_sectionCounts.GetValueOrDefault(zone.Id)})",
                    Id: zone.Id,
                    Glyph: GlyphOf(zone.Name)));
            }
        }

        if (found.Successes.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Section, "Succès", Combien(found.Successes.Count, "succès", "succès")));

            // Un succès ne se choisit pas : ce qu'on veut, ce sont ses quêtes.
            // Elles suivent donc son nom, dans l'ordre où l'on y joue.
            foreach (var success in found.Successes)
            {
                List<QuestSummary> quests =
                [
                    .. QuestPlayOrder.Sorted(_catalog.Catalog.Quests.Where(q =>
                        string.Equals(q.SuccessName, success, StringComparison.Ordinal))),
                ];

                Nodes.Add(new QuestNode(
                    QuestNodeKind.Success,
                    $"{success} ({quests.Count})",
                    QuestLevelRange.Of(quests),
                    Glyph: QuestNodeGlyph.Success));

                foreach (var quest in quests)
                {
                    Nodes.Add(ToNode(quest) with { InSuccess = true });
                }
            }
        }

        // Un groupe par nature, et seulement s'il a trouvé quelque chose : une
        // recherche ordinaire en montre un ou deux.
        foreach (var (kind, titre, un, plusieurs) in DungeonGroups)
        {
            List<DungeonSummary> places = [.. found.Of(kind)];

            if (places.Count == 0)
            {
                continue;
            }

            Nodes.Add(new QuestNode(QuestNodeKind.Section, titre, Combien(places.Count, un, plusieurs)));

            foreach (var place in places)
            {
                Nodes.Add(ToNode(place) with { Glyph = GlyphOf(kind) });
            }
        }

        if (found.Paths.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Section,
                "Chemins",
                Combien(found.Paths.Count, "chemin", "chemins")));

            foreach (var path in found.Paths)
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Quest, path.Title, Path: path, Glyph: QuestNodeGlyph.Route));
            }
        }

        if (found.Quests.Count > 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Section, "Quêtes", Nombre(found.Quests.Count)));

            foreach (var quest in found.Quests)
            {
                Nodes.Add(ToNode(quest));
            }
        }

        if (Nodes.Count == 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Pending, "Aucun résultat"));
        }
    }

    /// <summary>Les trois groupes de lieux de combat, dans l'ordre de la racine.</summary>
    private static readonly (DungeonKind Kind, string Title, string One, string Many)[] DungeonGroups =
    [
        (DungeonKind.Dungeon, "Donjons", "donjon", "donjons"),
        (DungeonKind.Raid, "Raids", "raid", "raids"),
        (DungeonKind.Lair, "Tanières", "tanière", "tanières"),
    ];

    /// <summary>La branche où un lieu de combat se trouve.</summary>
    private static int SectionOf(DungeonSummary place) => place.Kind switch
    {
        DungeonKind.Raid => RaidSection,
        DungeonKind.Lair => LairSection,
        _ => DungeonSection,
    };

    /// <summary>Les lieux de combat d'un genre, dans l'ordre du site.</summary>
    private IReadOnlyList<DungeonSummary> Fighting(DungeonKind kind) =>
        [.. _catalog.Catalog.Dungeons.Where(d => d.Kind == kind)];

    /// <summary>Les chemins d'un côté.</summary>
    private IReadOnlyList<PathSummary> Paths(PathSide side) =>
        [.. _catalog.Catalog.Paths.Where(p => p.Side == side)];

    /// <summary>
    /// La sous-branche des chemins, en tête de la branche qu'elle sert.
    ///
    /// Un dossier plutôt qu'un groupe à la suite : un chemin ne se compare ni à
    /// une zone ni à un donjon, et les mêler allongerait une liste qu'on
    /// parcourt déjà longuement.
    /// </summary>
    private QuestNode PathBranch(PathSide side, int section) => new(
        QuestNodeKind.Branch,
        "Chemins",
        Combien(Paths(side).Count, "chemin", "chemins"),
        Id: section,
        Glyph: QuestNodeGlyph.Route);

    /// <summary>
    /// Les donjons, du plus abordable au plus exigeant, coupés par paliers de
    /// cinquante niveaux.
    ///
    /// Quatre-vingt-trois lignes ne se parcourent pas d'un œil : on y cherche
    /// ce qui est à sa portée, et les paliers évitent de compter. Ceux dont le
    /// site ne donne pas le niveau ferment la marche sous leur propre
    /// intertitre, plutôt que de passer pour du niveau zéro.
    /// </summary>
    private IEnumerable<QuestNode> DungeonNodes()
    {
        var ordered = Fighting(DungeonKind.Dungeon)
            .OrderBy(d => DungeonLevelBand.RankOf(d.Level))
            .ThenBy(d => d.Level)
            .ThenBy(d => d.Title, StringComparer.CurrentCulture);

        var band = int.MinValue;

        foreach (var dungeon in ordered)
        {
            var rank = DungeonLevelBand.RankOf(dungeon.Level);

            if (rank != band)
            {
                band = rank;

                yield return new QuestNode(
                    QuestNodeKind.Header,
                    DungeonLevelBand.NameOf(dungeon.Level));
            }

            yield return ToNode(dungeon);
        }
    }

    /// <summary>
    /// Une ligne de donjon : son nom avec son niveau, et à droite ce qu'il faut
    /// savoir avant d'y aller.
    /// </summary>
    private static QuestNode ToNode(DungeonSummary dungeon) => new(
        QuestNodeKind.Quest,
        dungeon.Level > 0 ? $"{dungeon.Title} (niv. {dungeon.Level})" : dungeon.Title,
        Detail(dungeon),
        Dungeon: dungeon);

    /// <summary>
    /// Ce que la colonne de droite dit d'un donjon : la pierre d'âme et la
    /// position, précédées d'une clef quand il en faut une. Le nom de la clef
    /// vient au survol : il est trop long pour la colonne.
    /// </summary>
    private static string Detail(DungeonSummary dungeon)
    {
        List<string> parts = [];

        if (dungeon.SoulStone.Length > 0)
        {
            // « gigantesque pierre d'âme » dit deux fois « pierre d'âme » dans
            // une colonne où toutes les lignes en portent une : la taille suffit.
            parts.Add(dungeon.SoulStone.Replace(" pierre d’âme", string.Empty, StringComparison.Ordinal));
        }

        if (dungeon.Position.Length > 0)
        {
            parts.Add(dungeon.Position);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Rubriques qui contiennent au moins une quête, dans l'ordre où le site
    /// les range.
    ///
    /// Le catalogue les rend déjà ordonnées ; les reclasser par nombre de
    /// quêtes, comme on le faisait, revenait à ignorer l'ordre du site après
    /// être allé le chercher.
    /// </summary>
    private IEnumerable<QuestNode> Branches()
    {
        var zones = _catalog.Catalog.Sections
            .Where(s => s.Id != RootSection && _sectionCounts.GetValueOrDefault(s.Id) > 0)
            .OrderBy(s => QuestZoneOrder.RankOf(s.Name))
            .ThenBy(s => QuestZoneOrder.DisplayName(s.Name), StringComparer.CurrentCulture);

        List<QuestSection> ordered = [.. zones];
        var separated = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var zone = ordered[i];

            // Ce qui ne relève pas de la progression vient après un intertitre,
            // pour que la liste ne mélange pas un lieu et un cheminement.
            if (!separated && QuestZoneOrder.IsExtra(zone.Name))
            {
                separated = true;

                yield return new QuestNode(
                    QuestNodeKind.Header,
                    QuestZoneOrder.ExtrasHeader,
                    Glyph: QuestNodeGlyph.Family);
            }

            // Un blanc là où l'on passe d'une famille de quêtes aux lieux :
            // « Quêtes principales » ouvre la liste sans être un endroit, et
            // sans cette respiration elle se lit comme la première zone du
            // monde. Le blanc appartient à la ligne qui précède la rupture, et
            // non à celle qui la suit, pour ne pas doubler celui de
            // l'intertitre plus bas.
            var next = i + 1 < ordered.Count ? ordered[i + 1] : null;

            yield return new QuestNode(
                QuestNodeKind.Branch,
                $"{QuestZoneOrder.DisplayName(zone.Name)} ({_sectionCounts[zone.Id]})",
                QuestLevelRange.Of(_catalog.InSection(zone.Id)),
                Id: zone.Id,
                Glyph: GlyphOf(zone.Name),
                Spaced: next is not null
                    && !QuestZoneOrder.IsPlace(zone.Name)
                    && QuestZoneOrder.IsPlace(next.Name));
        }
    }

    private static string Text(int value) =>
        value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    /// Ajoute les quêtes d'une rubrique dans l'ordre où l'on y joue.
    ///
    /// C'est ainsi que le site les présente, et c'est ainsi qu'on les joue :
    /// une quête isolée dit rarement à quoi elle sert. Le rangement est celui
    /// de <see cref="QuestZonePlan"/>, qui suit les prérequis.
    /// </summary>
    private void AddBySuccess(IReadOnlyList<QuestSummary> quests)
    {
        var plan = QuestZonePlan.Of(quests, _catalog.Catalog.SuccessOrder);

        foreach (var block in plan)
        {
            if (block.IsSuccess)
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Success,
                    $"{block.SuccessName} ({block.Quests.Count})",
                    QuestLevelRange.Of(block.Quests),
                    Glyph: QuestNodeGlyph.Success));
            }

            foreach (var quest in block.Quests)
            {
                // Le décalage dit l'appartenance : une quête au ras de la marge
                // n'est réclamée par aucun succès.
                Nodes.Add(ToNode(quest) with { InSuccess = block.IsSuccess });
            }
        }
    }

    /// <summary>Combien d'étapes la page ouverte annonce.</summary>
    public int StepCount => _steps.Count;

    /// <summary>
    /// Une ligne de quête.
    ///
    /// La colonne de droite ne porte plus le niveau : le site ne le renseigne
    /// que sur cent dix-sept quêtes sur sept cent quatre-vingt-deux, et une
    /// colonne vide neuf fois sur dix ne mérite pas sa place. Elle porte les
    /// prérequis, qui en couvrent six cent treize, et qui disent quelque chose
    /// d'utile avant de partir : ce qu'il faut avoir fait.
    /// </summary>
    private QuestNode ToNode(QuestSummary quest) => new(
        QuestNodeKind.Quest,
        quest.Title,
        Quest: quest,
        Needs: NeedsOf(quest));

    /// <summary>
    /// Les prérequis d'une quête, chacun rattaché à la quête qu'il nomme quand
    /// c'en est une. Sur cinq cent soixante-sept prérequis distincts, beaucoup
    /// sont des objets, un alignement ou un créneau horaire : ceux-là restent
    /// du texte, et seuls les autres deviendront des liens.
    /// </summary>
    private IReadOnlyList<QuestNeed> NeedsOf(QuestSummary quest) =>
        quest.Prerequisites.Count == 0
            ? []
            : [.. quest.Prerequisites.Select(need => new QuestNeed(need, _chain?.Find(need)))];

    /// <summary>
    /// Ce que les prérequis relient, table construite une fois par catalogue.
    /// Sert aussi bien à rattacher un prérequis à sa quête qu'à prolonger la
    /// navigation au-delà d'un succès.
    /// </summary>
    private QuestChainIndex? _chain;

    /// <summary>L'icône d'une rubrique, selon qu'elle situe ou qu'elle range.</summary>
    private static QuestNodeGlyph GlyphOf(string? zone) =>
        QuestZoneOrder.IsPlace(zone) ? QuestNodeGlyph.Place : QuestNodeGlyph.Family;

    /// <summary>
    /// L'icône d'un lieu de combat, qui dit lequel des trois on regarde.
    ///
    /// Les trois se ressemblent assez pour partager un type ; ils ne se
    /// ressemblent pas assez pour partager une icône, la recherche pouvant
    /// rendre les trois d'un coup.
    /// </summary>
    private static QuestNodeGlyph GlyphOf(DungeonKind kind) => kind switch
    {
        DungeonKind.Raid => QuestNodeGlyph.Raids,
        DungeonKind.Lair => QuestNodeGlyph.Lairs,
        _ => QuestNodeGlyph.Dungeons,
    };

    /// <summary>Le nom de rubrique tel que le site l'écrit, pour en juger la nature.</summary>
    private string? RawNameOf(int section) =>
        _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == section)?.Name;

    private string NameOf(int section) =>
        QuestZoneOrder.DisplayName(
            _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == section)?.Name)
        is { Length: > 0 } name
            ? name
            : "Rubrique";

    private static string Nombre(int count) => count == 1 ? "1 quête" : $"{count} quêtes";

    /// <summary>
    /// Compte d'un intertitre de recherche. Le mot suit la nature : annoncer
    /// « 1 quête » au-dessus d'une zone ferait mentir l'intertitre juste
    /// au-dessus de ce qu'il coiffe.
    /// </summary>
    private static string Combien(int count, string singulier, string pluriel) =>
        count == 1 ? $"1 {singulier}" : $"{count} {pluriel}";

    /// <summary>Catégorie qui range toutes les quêtes du site.</summary>
    private const int RootSection = 7;

    /// <summary>
    /// Donne suite à un clic dans la liste. Rend l'adresse à charger, ou null
    /// quand le clic ne fait que déplier une branche.
    ///
    /// Une adresse et non une quête : la liste mène aussi bien à un donjon, et
    /// la fenêtre n'a besoin que de savoir quoi ouvrir.
    /// </summary>
    public string? Activate(QuestNode? node)
    {
        if (node is null || !node.IsEnabled)
        {
            return null;
        }

        switch (node.Kind)
        {
            case QuestNodeKind.Branch:
                Query = string.Empty;
                ShowSection(node.Id);
                return null;

            case QuestNodeKind.Quest when node.Quest is { } quest:
                SetCurrent(quest);
                IsListOpen = false;
                return quest.Url;

            case QuestNodeKind.Quest when node.Dungeon is { } dungeon:
                SetCurrent(dungeon);
                IsListOpen = false;
                return dungeon.Url;

            case QuestNodeKind.Quest when node.Path is { } path:
                SetCurrent(path);
                IsListOpen = false;
                return path.Url;

            default:
                return null;
        }
    }

    /// <summary>Retient la quête ouverte, pour le pied de fenêtre et le titre.</summary>
    public void SetCurrent(QuestSummary quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        _current = quest;
        _currentDungeon = null;
        _currentPath = null;
        CurrentUrl = quest.Url;
        QuestTitle = quest.Title;
        HasQuest = true;

        _start = QuestStepSummary.OfStart(quest.StartPosition, quest.StartPerson);
        _startsAtDeparture = false;

        SetNeighbours(quest);
        ExtendNeighbours(quest);

        // La page suivante n'est pas encore chargée : garder les étapes de la
        // précédente afficherait un objectif qui n'a plus rien à voir. Le
        // départ, lui, se sait déjà et tient la ligne en attendant.
        ResetSteps([]);
        SetStep(-1);

        StepDetail = _start ?? string.Empty;
    }

    /// <summary>
    /// Ouvre un donjon.
    ///
    /// Il n'a ni succès ni voisines : on n'enchaîne pas les donjons comme les
    /// quêtes d'une série, on en choisit un. Le bandeau ne porte donc que son
    /// nom et son départ, que le site donne dans ses métadonnées comme pour une
    /// quête.
    /// </summary>
    public void SetCurrent(DungeonSummary dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);

        _current = null;
        _currentDungeon = dungeon;
        _currentPath = null;
        CurrentUrl = dungeon.Url;
        QuestTitle = dungeon.Title;
        HasQuest = true;

        _start = QuestStepSummary.OfStart(dungeon.Position, dungeon.Person);
        _startsAtDeparture = false;

        ChainText = dungeon.Key.Length > 0 ? dungeon.Key : string.Empty;
        ChainStep = string.Empty;
        PreviousQuest = null;
        NextQuest = null;

        ResetSteps([]);
        SetStep(-1);

        StepDetail = _start ?? string.Empty;
    }

    /// <summary>
    /// Ouvre un chemin.
    ///
    /// Il n'a ni niveau ni voisines, et pas de départ à composer : un itinéraire
    /// commence là où on se trouve. Le bandeau ne porte donc que son nom, et les
    /// étapes viendront de ses titres de sections.
    /// </summary>
    public void SetCurrent(PathSummary path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _current = null;
        _currentDungeon = null;
        _currentPath = path;
        CurrentUrl = path.Url;
        QuestTitle = path.Title;
        HasQuest = true;

        _start = null;
        _startsAtDeparture = false;

        ChainText = "Chemin";
        ChainStep = string.Empty;
        PreviousQuest = null;
        NextQuest = null;

        ResetSteps([]);
        SetStep(-1);

        StepDetail = string.Empty;
    }

    /// <summary>
    /// Établit le succès de la quête ouverte, sa place et ses voisines.
    ///
    /// Ce sont les voisines de la liste du succès, dans l'ordre du site, et non
    /// celles de la chaîne publiée en pied de page : cette chaîne-là relie les
    /// prérequis, saute d'un succès à l'autre et se ramifie. La première quête
    /// d'un succès n'a pas de précédente, la dernière pas de suivante, et c'est
    /// ce qu'on attend en parcourant une liste.
    ///
    /// Une quête sans succès n'a pas de voisines : les autres quêtes de sa
    /// rubrique ne forment pas une suite.
    /// </summary>
    private void SetNeighbours(QuestSummary quest)
    {
        ChainText = quest.SuccessName;
        ChainStep = string.Empty;
        PreviousQuest = null;
        NextQuest = null;

        if (quest.SuccessName.Length == 0)
        {
            return;
        }

        List<QuestSummary> group =
        [
            .. QuestPlayOrder.Sorted(
                _catalog.Catalog.Quests.Where(q =>
                    string.Equals(q.SuccessName, quest.SuccessName, StringComparison.Ordinal))),
        ];

        var index = group.FindIndex(q =>
            string.Equals(q.Url, quest.Url, StringComparison.Ordinal));

        if (index < 0)
        {
            return;
        }

        ChainStep = $"{Text(index + 1)} / {Text(group.Count)}";

        if (index > 0)
        {
            PreviousQuest = ToLink(group[index - 1]);
        }

        if (index < group.Count - 1)
        {
            NextQuest = ToLink(group[index + 1]);
        }
    }

    /// <summary>
    /// Prolonge la navigation là où la liste du succès s'arrête, en suivant les
    /// prérequis.
    ///
    /// La première quête d'un succès n'a pas de précédente et la dernière pas
    /// de suivante ; une quête hors succès n'a ni l'une ni l'autre. Le site,
    /// lui, continue : « Bien débuter » mène à « Une arrivée mouvementée », qui
    /// mène à « Le début des problèmes », laquelle ouvre un succès. Mesuré, cela
    /// rend une suivante à cent soixante-huit quêtes et une précédente à cent
    /// quatre-vingt-dix-sept.
    /// </summary>
    private void ExtendNeighbours(QuestSummary quest)
    {
        if (_chain is null)
        {
            return;
        }

        PreviousQuest ??= ToLink(_chain.PreviousOf(quest), quest);
        NextQuest ??= ToLink(_chain.NextOf(quest), quest);

        // Et si rien ne pend à cette quête, la série suivante, cherchée dans
        // tout le succès : elle ne part pas toujours de sa dernière quête.
        NextQuest ??= ToLink(_chain.NextSeriesOf(quest), quest);
    }

    /// <summary>
    /// Le lien vers une quête voisine, annoncé par sa série quand on en change.
    ///
    /// Suivre un prérequis fait parfois entrer dans un autre succès, voire dans
    /// une autre zone. Le titre seul laisserait croire qu'on poursuit la même
    /// suite ; le nom du succès — à défaut celui de la zone — dit qu'on en
    /// commence une autre.
    /// </summary>
    private QuestLink? ToLink(QuestSummary? target, QuestSummary from)
    {
        if (target is null)
        {
            return null;
        }

        var link = ToLink(target);

        if (string.Equals(target.SuccessName, from.SuccessName, StringComparison.Ordinal))
        {
            // Par la rubrique qu'on parcourt, et non par celle que le catalogue
            // a retenue : deux quêtes de la même zone s'annonçaient d'une série
            // différente parce que l'une est aussi répétable.
            return SectionSeen(target) == SectionSeen(from)
                ? link
                : link with { Series = NameOf(SectionSeen(target)) };
        }

        return link with
        {
            Series = target.SuccessName.Length > 0 ? target.SuccessName : NameOf(SectionSeen(target)),
        };
    }

    private static QuestLink ToLink(QuestSummary quest) =>
        new(quest.Title, quest.Url, QuestLinkKind.Quest);

    /// <summary>Étapes repérées dans la page ouverte.</summary>
    private IReadOnlyList<string> _steps = [];

    /// <summary>
    /// Les étapes du guide, telles qu'on les choisit dans la liste que le rang
    /// déplie. Les deux flèches n'avancent que d'une à la fois.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<QuestStepRowViewModel> Steps { get; } = [];

    /// <summary>
    /// Raccourci de la première étape, composé des métadonnées de la quête
    /// ouverte. Null quand le site ne dit ni où ni auprès de qui elle se lance.
    /// </summary>
    private string? _start;

    /// <summary>
    /// Vrai quand la première étape est le départ de la quête et non un
    /// paragraphe du guide. Le pont l'annonce, parce que lui seul voit si la
    /// page porte un bloc de départ.
    /// </summary>
    private bool _startsAtDeparture;

    [ObservableProperty]
    private int _stepIndex = -1;

    /// <summary>« Étape 3 / 7 », ou rien quand la page n'a pas d'étape.</summary>
    [ObservableProperty]
    private string _stepText = string.Empty;

    /// <summary>Ce qu'il y a à faire à cette étape, en une ligne.</summary>
    [ObservableProperty]
    private string _stepDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSteps))]
    private bool _hasSteps;

    [ObservableProperty]
    private bool _canGoPreviousStep;

    [ObservableProperty]
    private bool _canGoNextStep;

    /// <summary>Quête suivante du succès, s'il y en a une après celle-ci.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    private QuestLink? _nextQuest;

    /// <summary>Quête précédente du succès, s'il y en a une avant celle-ci.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    private QuestLink? _previousQuest;

    /// <summary>
    /// Ce que la page vient de livrer : ses blocs structurés et ses étapes.
    /// </summary>
    public void SetPage(
        string? introHtml,
        string? chainHtml,
        IReadOnlyList<string> steps,
        bool startsAtDeparture = false)
    {
        ArgumentNullException.ThrowIfNull(steps);

        _startsAtDeparture = startsAtDeparture;

        var facts = QuestPageParser.ParseFacts(introHtml);
        var chain = QuestPageParser.ParseChain(chainHtml);

        // La chaîne publiée en pied de page relie les prérequis : elle saute
        // d'un succès à l'autre et se ramifie. « Les rescapés de Frigost » y a
        // deux suites et pour seul précédent un jalon. Ce n'est pas ce qu'on
        // parcourt : les voisines sont celles de la liste du succès, et
        // SetCurrent les a déjà posées avant même que la page arrive.
        //
        // Le nom du succès vient du catalogue pour la même raison, afin que la
        // liste et la page s'accordent. On ne retombe sur celui de la page que
        // pour une quête que le catalogue ne rattache à rien.
        if (ChainText.Length == 0)
        {
            ChainText = facts.Success ?? string.Empty;
        }


        ResetSteps(steps);

        SetStep(steps.Count > 0 ? 0 : -1);
    }

    /// <summary>Le défilement a changé d'étape, ou l'utilisateur en a choisi une.</summary>
    public void SetStep(int index)
    {
        StepIndex = index;

        var total = _steps.Count;

        StepText = index >= 0 && total > 0
            ? $"Étape {index + 1} / {total}"
            : string.Empty;

        StepDetail = index >= 0 && index < total ? StepLabel(index) : string.Empty;

        foreach (var row in Steps)
        {
            row.IsCurrent = row.Index == index;
        }

        CanGoPreviousStep = index > 0;
        CanGoNextStep = index >= 0 && index < total - 1;
    }

    /// <summary>
    /// Ce qu'il y a à faire à cette étape, en une ligne.
    ///
    /// Le texte brut du paragraphe tenait sur une ligne tronquée où l'on ne
    /// voyait ni où aller ni à qui parler : chaque étape est donc résumée.
    ///
    /// La première l'est par les métadonnées de la quête, plus sûres que la
    /// prose du site, mais seulement quand c'est bien le départ : le pont le
    /// dit. Sans cette réserve, le départ se retrouvait annoncé au-dessus du
    /// premier paragraphe du guide, qui n'a le plus souvent rien à voir.
    /// </summary>
    private string StepLabel(int index) =>
        index == 0 && _startsAtDeparture
            ? _start ?? QuestStepSummary.Of(_steps[0])
            : QuestStepSummary.Of(_steps[index]);

    /// <summary>
    /// Repose les étapes, et la liste où on les choisit avec elles.
    ///
    /// Une seule porte pour les deux : la liste et le compte se contredisaient
    /// dès qu'un chemin oubliait l'une des deux lignes.
    /// </summary>
    private void ResetSteps(IReadOnlyList<string> steps)
    {
        _steps = steps;
        HasSteps = steps.Count > 0;

        Steps.Clear();

        for (var index = 0; index < steps.Count; index++)
        {
            Steps.Add(new QuestStepRowViewModel(index, StepLabel(index)));
        }
    }

    /// <summary>
    /// On suit un lien de succès. Le titre est repris tout de suite : la page
    /// met une seconde à répondre, et un bandeau qui garde l'ancien nom pendant
    /// ce temps laisse croire que le clic n'a rien fait.
    ///
    /// La quête est retrouvée dans le catalogue par son adresse, pour que le
    /// pied reste peuplé. Il ne l'était pas : la méthode effaçait la précédente
    /// et la suivante sans jamais les rétablir, si bien qu'après un seul saut
    /// la navigation s'éteignait et qu'il fallait repasser par la liste.
    /// </summary>
    public void Follow(QuestLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (TryFollowUrl(link.Url))
        {
            return;
        }

        // Une adresse que le catalogue ne connaît pas : on ouvre quand même,
        // sans voisines, plutôt que de ne rien faire.
        CurrentUrl = link.Url;
        QuestTitle = link.Title;
        ChainText = string.Empty;
        ChainStep = string.Empty;
        HasQuest = true;
        IsListOpen = false;

        _start = null;
        _startsAtDeparture = false;
        ResetSteps([]);
        PreviousQuest = null;
        NextQuest = null;
        SetStep(-1);
    }

    /// <summary>
    /// Suit une adresse sur place quand le catalogue la connaît, et dit si elle
    /// l'était.
    ///
    /// Un guide renvoie vers ses quêtes voisines par de simples liens : un clic
    /// dessus ouvrait une seconde fenêtre alors que le bouton « précédente »,
    /// qui mène au même endroit, restait sur place. Le catalogue tranche : ce
    /// qu'il connaît se suit ici, le reste part à part.
    /// </summary>
    public bool TryFollowUrl(string? url, bool remember = false)
    {
        var key = UrlKey(url);

        if (key.Length == 0)
        {
            return false;
        }

        var quest = _catalog.Catalog.Quests.FirstOrDefault(q =>
            string.Equals(UrlKey(q.Url), key, StringComparison.Ordinal));

        if (quest is null)
        {
            // Un donjon se suit comme une quête : c'est une page du site que le
            // catalogue connaît. Sans cela, le lien d'un guide vers un donjon
            // partait dans une fenêtre à part, et la dernière page lue n'était
            // pas retrouvée au lancement suivant.
            var dungeon = _catalog.Catalog.Dungeons.FirstOrDefault(d =>
                string.Equals(UrlKey(d.Url), key, StringComparison.Ordinal));

            if (dungeon is not null)
            {
                SetCurrent(dungeon);
                IsListOpen = false;

                return true;
            }

            var path = _catalog.Catalog.Paths.FirstOrDefault(p =>
                string.Equals(UrlKey(p.Url), key, StringComparison.Ordinal));

            if (path is null)
            {
                return false;
            }

            SetCurrent(path);
            IsListOpen = false;

            return true;
        }

        // Seul un lien suivi sur place entre dans l'historique. Les voisines du
        // pied et le choix dans la liste n'y entrent pas : on sait d'où l'on
        // vient quand c'est soi qui a désigné où aller, et la flèche resterait
        // allumée en permanence pour ne rien dire.
        if (remember
            && _current is { } left
            && !string.Equals(left.Url, quest.Url, StringComparison.Ordinal))
        {
            _visited.Push(left);
            CanGoBackQuest = true;
        }

        SetCurrent(quest);
        IsListOpen = false;

        return true;
    }

    /// <summary>
    /// Adresse réduite à ce qui l'identifie.
    ///
    /// Le site écrit ses liens tantôt avec la barre finale, tantôt sans, et la
    /// comparaison stricte manquait alors une quête pourtant au catalogue.
    /// </summary>
    private static string UrlKey(string? url) =>
        (url ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();

    /// <summary>
    /// Rang de la première ligne qui se choisit, en enjambant les intertitres.
    /// Rend -1 si la liste n'offre rien.
    /// </summary>
    public int FirstSelectable()
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i].IsEnabled)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Étape visée par une flèche, ou -1 s'il n'y a nulle part où aller.</summary>
    public int StepTarget(int direction)
    {
        var target = StepIndex + direction;

        return target >= 0 && target < _steps.Count ? target : -1;
    }

    /// <summary>
    /// Le navigateur embarqué n'a pas pu se mettre en route.
    ///
    /// Le message de l'incident porte déjà sa cause quand elle est connue :
    /// l'environnement dit lui-même que le composant WebView2 manque, et
    /// où le prendre. On n'y ajoute que le repli, qui vaut dans tous les cas.
    /// </summary>
    public void ReportViewFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        HasQuest = false;
        IsLoadingPage = false;
        Placeholder =
            exception.Message
            + "\n\nEn attendant, le bouton du bas ouvre la page dans votre navigateur.";
    }

    /// <summary>L'accueil du site.</summary>
    private const string HomeUrl = "https://papycha.fr/";

    /// <summary>La page du site qui énumère les zones de quêtes.</summary>
    private const string IndexUrl = "https://papycha.fr/quetes/";

    /// <summary>
    /// Rouvre sur le site ce que la fenêtre montre, et non la quête en toutes
    /// circonstances.
    ///
    /// Le bouton menait toujours à la quête courante, y compris quand la liste
    /// couvrait l'écran sur une rubrique qu'on parcourait : il ouvrait alors
    /// autre chose que ce qu'on avait sous les yeux, ou rien du tout tant
    /// qu'aucune quête n'avait été choisie.
    ///
    /// Une recherche fait exception : elle ne montre pas de rubrique, et ce
    /// qu'on lisait avant de la lancer reste la quête.
    /// </summary>
    public void OpenInBrowser()
    {
        var url = Browsed() ?? CurrentUrl;

        if (!string.IsNullOrWhiteSpace(url))
        {
            _dialogs.OpenUrl(url);
        }
    }

    /// <summary>
    /// Ce qu'il faut pour signaler une erreur sur ce qu'on a sous les yeux :
    /// la page, et le repère d'étape à porter dans le formulaire du site.
    /// Rend <c>null</c> quand il n'y a pas de page du site à signaler.
    ///
    /// La page est celle qu'« Ouvrir dans le navigateur » ouvrirait, et pour la
    /// même raison : on signale ce qu'on lit, pas la quête en toutes
    /// circonstances.
    /// </summary>
    public (string Url, string Location)? ErrorReport() =>
        CanReport
            ? (CurrentUrl!, PapychaReport.Location(StepIndex, _steps.Count, StepTextAt(StepIndex)))
            : null;

    /// <summary>
    /// Vrai quand il y a un formulaire à ouvrir, c'est-à-dire quand un guide
    /// est sous les yeux.
    ///
    /// Le site ne met de formulaire de signalement qu'en pied d'article, mesuré
    /// page par page : les rubriques n'en ont pas, et il n'en existe pas de
    /// général. Le bouton disparaît donc plutôt que de mener nulle part.
    /// </summary>
    public bool CanReport => !IsListOpen && HasQuest && PapychaSite.Owns(CurrentUrl);

    private string? StepTextAt(int index) =>
        index >= 0 && index < _steps.Count ? _steps[index] : null;

    /// <summary>
    /// La page de ce que la liste parcourt, ou <c>null</c> si elle ne parcourt
    /// rien : liste fermée, recherche en cours, ou rubrique sans page rédigée.
    /// </summary>
    private string? Browsed()
    {
        if (!IsListOpen || Query.Length > 0)
        {
            return null;
        }

        // La racine du menu n'est pas la liste des zones : elle annonce les
        // quêtes et les donjons, soit tout ce que le site offre. C'est donc son
        // accueil qu'elle ouvre, et la page des zones un cran plus bas.
        if (_section == 0)
        {
            return HomeUrl;
        }

        if (_section == RootSection)
        {
            return IndexUrl;
        }

        var url = _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == _section)?.Url;

        // Trois rubriques sur vingt-cinq n'ont pas de page à elles : le tableau
        // du site ne les nomme pas. On reste alors sur celle qui les énumère
        // toutes, plutôt que de renvoyer vers une quête dont il n'est pas
        // question à l'écran.
        return string.IsNullOrWhiteSpace(url) ? IndexUrl : url;
    }
}

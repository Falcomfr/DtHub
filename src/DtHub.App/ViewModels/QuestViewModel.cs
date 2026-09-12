using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Localization;
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
        _tree = new QuestTree(catalog, _sectionCounts);
        _dialogs = dialogs;
    }

    /// <summary>
    /// Ce que la liste déroulante montre à cet instant : les branches, les
    /// quêtes d'une rubrique, ou les résultats d'une recherche.
    /// </summary>
    public ObservableCollection<QuestNode> Nodes { get; } = [];

    /// <summary>Nombre de quêtes du catalogue rangées dans chaque rubrique.</summary>
    private readonly Dictionary<int, int> _sectionCounts = [];

    /// <summary>Les branches, bâties depuis le catalogue et ce décompte.</summary>
    private readonly QuestTree _tree;

    /// <summary>Combien d'étapes la page ouverte annonce.</summary>
    public int StepCount => _steps.Count;

    /// <summary>Pose dans la liste les lignes que l'arbre compose.</summary>
    private void AddBySuccess(IReadOnlyList<QuestSummary> quests)
    {
        foreach (var node in _tree.BySuccess(quests))
        {
            Nodes.Add(node);
        }
    }

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
    private readonly Stack<string> _visited = new();

    /// <summary>
    /// Ce sur quoi la liste se repose : la rubrique et l'adresse de la dernière
    /// page qu'on a désignée soi-même, dans la liste ou par les boutons du pied.
    ///
    /// Un lien suivi dans le guide ne les déplace pas : on va voir un chemin ou
    /// un donjon, et l'on veut retrouver la fiche d'où l'on vient en rouvrant le
    /// panneau. Sans ce repère, ouvrir un chemin depuis une quête rouvrait la
    /// liste sur la branche des chemins, et plus rien ne disait d'où l'on
    /// venait.
    /// </summary>
    private int? _anchorSection;

    private string? _anchorUrl;

    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>
    /// Le texte pour lequel on propose la recherche du site, vide quand on ne
    /// cherche rien.
    ///
    /// Notre catalogue ne connaît que des titres. Chercher un objet, un monstre
    /// ou un personnage n'y donne rien, alors que le site le trouve : il cherche
    /// dans le corps de ses articles. L'offre suit donc la recherche, dès la
    /// première lettre : elle a d'abord paru sur un retour à la ligne, et l'écran
    /// qui en avait le plus besoin, celui qui annonce « aucun résultat », était
    /// justement celui qui ne l'avait pas.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SiteSearchLabel))]
    [NotifyPropertyChangedFor(nameof(ShowsSiteSearch))]
    private string _siteSearch = string.Empty;

    /// <summary>Ce que le lien annonce, le texte cherché compris.</summary>
    public string SiteSearchLabel =>
        SiteSearch.Length == 0 ? string.Empty : Strings.Format("SearchOnSite", SiteSearch);

    /// <summary>
    /// Vrai quand l'offre de chercher sur le site a lieu d'être : un texte
    /// cherché, et la liste sous les yeux.
    ///
    /// **Elle survivait au guide qu'on venait d'ouvrir.** Le texte cherché
    /// n'est effacé qu'en descendant vers une rubrique ; ouvrir une quête
    /// depuis un résultat le laisse en place, et le pied continuait donc de
    /// proposer « Chercher … sur papycha.fr » au bas d'un guide déjà ouvert,
    /// alors que la recherche était finie et avait abouti.
    ///
    /// **Le premier remède visait à côté.** Il exigeait qu'aucune quête ne soit
    /// ouverte, et faisait donc disparaître l'offre dès qu'on cherchait depuis
    /// un guide, ce qui est le geste le plus courant : on lit, on veut autre
    /// chose, on tape. La question n'est pas de savoir si une page est ouverte
    /// mais si la liste est à l'écran.
    ///
    /// <see cref="IsListOpen" /> répond exactement à cela, et taper du texte le
    /// pose. L'offre paraît donc sur l'écran des résultats, et surtout sur
    /// celui qui annonce « aucun résultat » : c'est là qu'on veut aller voir
    /// ailleurs. Devant un guide seul, elle n'offre rien que le lecteur
    /// cherche.
    /// </summary>
    public bool ShowsSiteSearch => SiteSearch.Length > 0 && IsListOpen;

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
    [NotifyPropertyChangedFor(nameof(CanCloseList))]
    private bool _hasQuest;

    /// <summary>Ce qu'on lit tant qu'aucune quête n'est ouverte.</summary>
    [ObservableProperty]
    private string _placeholder = Strings.Get("SearchPlaceholder");

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
    [NotifyPropertyChangedFor(nameof(CanCloseList))]
    [NotifyPropertyChangedFor(nameof(ShowsSiteSearch))]
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
    /// Vrai si refermer la liste mène quelque part.
    ///
    /// La croix rend la place au guide qu'on lisait. Sans guide ouvert, elle ne
    /// rend rien : elle échange une liste utilisable contre une fenêtre vide
    /// qui invite à rouvrir cette même liste. Une commande dont le seul effet
    /// est d'annuler ce qu'on vient de faire n'a pas à être proposée.
    /// </summary>
    public bool CanCloseList => IsListOpen && HasQuest;

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

        // **L'arbre d'hier vaut mieux qu'un arbre vide.** Le catalogue en cache
        // est déjà chargé en mémoire quand on arrive ici, et pourtant la
        // fenêtre affichait « Quêtes 0 » pendant toute la relecture : elle
        // attendait la fin d'une lecture réseau pour montrer ce qu'elle avait
        // déjà sur le disque. On le montre tout de suite, la ligne d'état
        // disant par ailleurs qu'une mise à jour est en cours.
        if (before.Quests > 0)
        {
            CountSections();
            ShowRoot();
        }

        var progress = new Progress<QuestIndexingProgress>(
            p => StatusText = QuestIndexingLabel.For(p));

        var catalog = await _catalog.GetAsync(progress, cancellationToken).ConfigureAwait(true);

        Settle(catalog, before);
        CountSections();
        ShowRoot();
    }

    /// <summary>
    /// Durée de la dernière indexation complète, ou <c>null</c> s'il n'y en a
    /// pas eu. Journalisée par la fenêtre, la vue-modèle n'ayant pas de journal.
    /// </summary>
    public TimeSpan? LastIndexing => _catalog.LastIndexing;

    /// <summary>
    /// Range ce qui suit une lecture : la chaîne et l'état affiché.
    /// </summary>
    private void Settle(QuestCatalogDocument catalog, QuestTally before)
    {
        _tree.Chain = new QuestChainIndex(catalog.Quests);

        IsBusy = false;

        // Ce que la relecture a rapporté, quand il y avait quelque chose avant
        // à quoi le comparer. La première indexation se tait : annoncer « sept
        // cent quatre-vingt-deux quêtes de plus » n'apprendrait rien.
        //
        // C'est dit sans qu'on l'ait demandé, parce que personne ne demande une
        // relecture : la sentinelle décide, et l'on veut savoir ce qu'elle a
        // trouvé.
        StatusText = catalog.Quests.Count == 0
            ? Strings.Get("NoQuestSiteDown")
            : _catalog.LastFailure is not null
                ? Strings.Get("SiteDownCached")
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
        // L'offre suit le texte tapé, mot à mot : c'est la même chose que ce
        // qu'on cherche, donc elle ne peut pas s'en écarter.
        SiteSearch = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

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
        var section = _anchorSection ?? SectionOfPage();

        if (section is { } target && (_section != target || Query.Length > 0))
        {
            Query = string.Empty;
            ShowSection(target);
        }

        SelectCurrent();

        IsListOpen = true;
    }

    /// <summary>Pose le repère de la liste sur la page qu'on vient de désigner.</summary>
    private void Anchor(int section, string url)
    {
        _anchorSection = section;
        _anchorUrl = url;
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
        // Le repère plutôt que la page courante : après un lien suivi, c'est la
        // fiche d'où l'on vient qu'on veut retrouver surlignée.
        var vise = _anchorUrl ?? CurrentUrl;

        if (string.IsNullOrEmpty(vise))
        {
            return;
        }

        SelectedNode = Nodes.FirstOrDefault(n =>
            n.Url is { } url && string.Equals(url, vise, StringComparison.Ordinal));
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
            Strings.Get("Quests"),
            QuestTree.Nombre(_catalog.Catalog.Quests.Count),
            Id: QuestTree.RootSection,
            Glyph: QuestNodeGlyph.Quests));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            Strings.Get("Dungeons"),
            QuestTree.Combien(_tree.Fighting(DungeonKind.Dungeon).Count, "WordDungeon", "WordDungeons"),
            Id: QuestTree.DungeonSection,
            Glyph: QuestNodeGlyph.Dungeons));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            Strings.Get("Raids"),
            QuestTree.Combien(_tree.Fighting(DungeonKind.Raid).Count, "WordRaid", "WordRaids"),
            Id: QuestTree.RaidSection,
            Glyph: QuestNodeGlyph.Raids));
        Nodes.Add(new QuestNode(
            QuestNodeKind.Branch,
            Strings.Get("Lairs"),
            QuestTree.Combien(_tree.Fighting(DungeonKind.Lair).Count, "WordLair", "WordLairs"),
            Id: QuestTree.LairSection,
            Glyph: QuestNodeGlyph.Lairs));
    }


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

        if (section == QuestTree.DungeonSection)
        {
            Breadcrumb = Strings.Get("Dungeons");
            SetBack(target: 0);

            Nodes.Add(_tree.PathBranch(PathSide.Dungeons, QuestTree.DungeonPathSection));

            foreach (var node in _tree.DungeonNodes())
            {
                Nodes.Add(node);
            }

            return;
        }

        if (section is QuestTree.RaidSection or QuestTree.LairSection)
        {
            var kind = section == QuestTree.RaidSection ? DungeonKind.Raid : DungeonKind.Lair;

            Breadcrumb = Strings.Get(section == QuestTree.RaidSection ? "Raids" : "Lairs");
            SetBack(target: 0);

            // Sans paliers : dix lignes se lisent d'un trait, et les couper
            // n'aiderait personne.
            foreach (var place in _tree.Fighting(kind).OrderBy(d => d.Level)
                         .ThenBy(d => d.Title, StringComparer.CurrentCulture))
            {
                Nodes.Add(QuestTree.NodeOf(place));
            }

            return;
        }

        if (section is QuestTree.QuestPathSection or QuestTree.DungeonPathSection)
        {
            var side = section == QuestTree.DungeonPathSection ? PathSide.Dungeons : PathSide.Quests;

            Breadcrumb = Strings.Get(side == PathSide.Dungeons ? "Dungeons" : "QuestAreaCrumb")
                + Separator + Strings.Get("Paths");
            SetBack(target: side == PathSide.Dungeons ? QuestTree.DungeonSection : QuestTree.RootSection);

            foreach (var path in _tree.Paths(side).OrderBy(p => p.Title, StringComparer.CurrentCulture))
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Quest, path.Title, Path: path, Glyph: QuestNodeGlyph.Route));
            }

            return;
        }

        if (section == QuestTree.RootSection)
        {
            Breadcrumb = Strings.Get("QuestAreaCrumb");
            SetBack(target: 0);

            Nodes.Add(_tree.PathBranch(PathSide.Quests, QuestTree.QuestPathSection));

            foreach (var branch in _tree.Branches())
            {
                Nodes.Add(branch);
            }

            return;
        }

        Breadcrumb = Strings.Get("QuestAreaCrumb") + Separator + _tree.NameOf(section);
        SetBack(target: QuestTree.RootSection);

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

    /// <summary>Vrai quand l'historique des pages lues a de quoi revenir.</summary>
    [ObservableProperty]
    private bool _canGoBackPage;

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
    public string? GoBackPage()
    {
        if (_visited.Count == 0)
        {
            return null;
        }

        var url = _visited.Pop();

        CanGoBackPage = _visited.Count > 0;

        // Par la même porte que l'aller : seules des adresses que le catalogue
        // sait rouvrir sont empilées, et c'est lui qui décide de la nature.
        TryFollowUrl(url);

        IsListOpen = false;

        return url;
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
        Breadcrumb = Strings.Get("Search");
        ClearBack();

        Nodes.Clear();

        var found = _catalog.SearchAll(query, limit: 60);

        if (found.Zones.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Section, Strings.Get("Zones"), QuestTree.Combien(found.Zones.Count, "WordZone", "WordZones")));

            foreach (var zone in found.Zones)
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Branch,
                    $"{QuestZoneOrder.DisplayName(zone.Name)} ({_sectionCounts.GetValueOrDefault(zone.Id)})",
                    Id: zone.Id,
                    Glyph: QuestTree.GlyphOf(zone.Name)));
            }
        }

        if (found.Successes.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Section,
                Strings.Get("Achievements"),
                QuestTree.Combien(found.Successes.Count, "WordAchievement", "WordAchievements")));

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
                    Nodes.Add(_tree.NodeOf(quest) with { InSuccess = true });
                }
            }
        }

        // Un groupe par nature, et seulement s'il a trouvé quelque chose : une
        // recherche ordinaire en montre un ou deux.
        foreach (var (kind, titre, un, plusieurs) in QuestTree.DungeonGroups)
        {
            List<DungeonSummary> places = [.. found.Of(kind)];

            if (places.Count == 0)
            {
                continue;
            }

            Nodes.Add(new QuestNode(
                QuestNodeKind.Section, Strings.Get(titre), QuestTree.Combien(places.Count, un, plusieurs)));

            foreach (var place in places)
            {
                Nodes.Add(QuestTree.NodeOf(place) with { Glyph = QuestTree.GlyphOf(kind) });
            }
        }

        if (found.Paths.Count > 0)
        {
            Nodes.Add(new QuestNode(
                QuestNodeKind.Section,
                Strings.Get("Paths"),
                QuestTree.Combien(found.Paths.Count, "WordPath", "WordPaths")));

            foreach (var path in found.Paths)
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Quest, path.Title, Path: path, Glyph: QuestNodeGlyph.Route));
            }
        }

        if (found.Quests.Count > 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Section, Strings.Get("Quests"), QuestTree.Nombre(found.Quests.Count)));

            foreach (var quest in found.Quests)
            {
                Nodes.Add(_tree.NodeOf(quest));
            }
        }

        if (Nodes.Count == 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Pending, Strings.Get("NoResult")));
        }
    }

    /// <summary>
    /// Les trois groupes de lieux de combat, dans l'ordre de la racine. Les
    /// quatre textes sont des clés de traduction et non des libellés : la
    /// table est statique, et la langue n'est connue qu'à l'affichage.
    /// </summary>


    /// <summary>Ce qui sépare deux niveaux d'un fil d'Ariane.</summary>
    private const string Separator = QuestTree.Separator;


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

        // Choisir dans la liste efface la piste : on a désigné où aller, et ce
        // qu'on lisait avant ne veut plus rien dire. La flèche restait sinon
        // allumée, pointant une page sans rapport.
        _visited.Clear();
        CanGoBackPage = false;

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
    /// <param name="anchor">
    /// Faux quand on suit un lien du guide : la page s'affiche, mais le repère
    /// de la liste reste sur la fiche d'où l'on vient.
    /// </param>
    public void SetCurrent(QuestSummary quest, bool anchor = true)
    {
        ArgumentNullException.ThrowIfNull(quest);

        _current = quest;
        _currentDungeon = null;
        _currentPath = null;

        if (anchor)
        {
            Anchor(SectionSeen(quest), quest.Url);
        }
        CurrentUrl = quest.Url;
        QuestTitle = quest.Title;
        HasQuest = true;

        _start = QuestStepSummary.OfStart(quest.StartPosition, quest.StartPerson);
        _startsAtDeparture = false;

        SetNeighbours(quest);

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
    /// <param name="anchor">Voir la surcharge des quêtes.</param>
    public void SetCurrent(DungeonSummary dungeon, bool anchor = true)
    {
        ArgumentNullException.ThrowIfNull(dungeon);

        _current = null;
        _currentDungeon = dungeon;
        _currentPath = null;

        if (anchor)
        {
            Anchor(QuestTree.SectionOf(dungeon), dungeon.Url);
        }
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
    /// <param name="anchor">Voir la surcharge des quêtes.</param>
    public void SetCurrent(PathSummary path, bool anchor = true)
    {
        ArgumentNullException.ThrowIfNull(path);

        _current = null;
        _currentDungeon = null;
        _currentPath = path;

        if (anchor)
        {
            Anchor(
                path.Side == PathSide.Dungeons ? QuestTree.DungeonPathSection : QuestTree.QuestPathSection,
                path.Url);
        }
        CurrentUrl = path.Url;
        QuestTitle = path.Title;
        HasQuest = true;

        _start = null;
        _startsAtDeparture = false;

        ChainText = Strings.Get("Path");
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
    /// Le choix des voisines est dans le noyau, <see cref="QuestNeighbourhood"/> ;
    /// il ne reste ici que l'habillage, qui demande de savoir quelle rubrique
    /// on parcourt. Ce sont les voisines que le catalogue connaît : la page,
    /// quand elle arrivera, dira ce que le site publie et l'emportera.
    /// </summary>
    /// <summary>
    /// Ce que le catalogue a décidé pour la quête ouverte. Retenu parce que la
    /// page, en arrivant, doit savoir si la liste du succès avait déjà tranché.
    /// </summary>
    private QuestNeighbours _neighbours;

    private void SetNeighbours(QuestSummary quest)
    {
        var neighbours = QuestNeighbourhood.Of(quest, _catalog.Catalog.Quests, _tree.Chain);

        _neighbours = neighbours;

        ChainText = quest.SuccessName;

        ChainStep = neighbours.Count > 0
            ? $"{QuestTree.Text(neighbours.Rank)} / {QuestTree.Text(neighbours.Count)}"
            : string.Empty;

        PreviousQuest = ToLink(neighbours.Previous, quest);
        NextQuest = ToLink(neighbours.Next, quest);
    }

    /// <summary>
    /// Le lien vers une quête voisine, annoncé par sa série quand on en change.
    ///
    /// Suivre un prérequis fait parfois entrer dans un autre succès, voire dans
    /// une autre zone. Le titre seul laisserait croire qu'on poursuit la même
    /// suite ; le nom du succès - à défaut celui de la zone - dit qu'on en
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
                : link with { Series = _tree.NameOf(SectionSeen(target)) };
        }

        return link with
        {
            Series = target.SuccessName.Length > 0 ? target.SuccessName : _tree.NameOf(SectionSeen(target)),
        };
    }

    private static QuestLink ToLink(QuestSummary quest) =>
        new(quest.Title, quest.Url, QuestLinkKind.Quest);

    /// <summary>
    /// La voisine que le site nomme, rendue avec son étiquette de série quand
    /// le catalogue la connaît.
    ///
    /// Il la connaît presque toujours, et l'étiquette est ce qui prévient qu'on
    /// change de succès ou de zone. Quand il ne la connaît pas, on garde le
    /// titre et l'adresse du site plutôt que de taire la suite : le bouton
    /// mène alors à une page que la fenêtre ouvrira à part.
    /// </summary>
    private QuestLink? Published(QuestLink? published, QuestSummary from)
    {
        if (published is null)
        {
            return null;
        }

        var key = UrlKey(published.Url);

        var target = _catalog.Catalog.Quests.FirstOrDefault(q =>
            string.Equals(UrlKey(q.Url), key, StringComparison.Ordinal));

        return target is null ? published : ToLink(target, from);
    }

    /// <summary>Étapes repérées dans la page ouverte.</summary>
    private IReadOnlyList<QuestStep> _steps = [];

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
        IReadOnlyList<QuestStep> steps,
        bool startsAtDeparture = false)
    {
        ArgumentNullException.ThrowIfNull(steps);

        _startsAtDeparture = startsAtDeparture;

        var facts = QuestPageParser.ParseFacts(introHtml);
        var chain = QuestPageParser.ParseChain(chainHtml);

        // Le site publie en pied d'article ce qui précède et ce qui suit, et
        // c'est lui qui fait foi **là où nous n'avons rien** : aux bornes d'un
        // succès et pour les quêtes qui n'en ont pas. SetCurrent a posé notre
        // ordre avant que la page arrive, pour que les boutons répondent
        // pendant le chargement ; la page le complète en arrivant.
        //
        // Mais elle ne le remplace plus à l'intérieur d'une liste. Cette
        // colonne s'intitule « Quêtes et jalons suivants » : elle dit ce que la
        // quête débloque, non l'ordre où on lit un succès. Dans « Le théâtre
        // des gobelins », celle de « Titi Gobelait le magobelin » ne nomme que
        // « Manque de moule », qui l'exige, et suivre la colonne sautait « Un
        // avenir de krotte de Trooll », qui vient avant et n'exige rien.
        //
        // Et jamais quand la colonne nomme plusieurs quêtes : en désigner une
        // mentirait.
        if (_current is { } ouverte)
        {
            if (!_neighbours.PreviousFromList)
            {
                PreviousQuest = Published(chain.OnlyPreviousQuest, ouverte) ?? PreviousQuest;
            }

            if (!_neighbours.NextFromList)
            {
                NextQuest = Published(chain.OnlyNextQuest, ouverte) ?? NextQuest;
            }
        }

        // Le nom du succès vient du catalogue, afin que la liste et la page
        // s'accordent. On ne retombe sur celui de la page que pour une quête
        // que le catalogue ne rattache à rien.
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

        StepText = index >= 0 && index < total ? StepRank(index) : string.Empty;

        StepDetail = index >= 0 && index < total ? StepLabel(index) : string.Empty;

        foreach (var row in Steps)
        {
            row.IsCurrent = row.Index == index;
        }

        CanGoPreviousStep = index > 0;
        CanGoNextStep = index >= 0 && index < total - 1;
    }

    /// <summary>
    /// Ce qu'on écrit à côté du rang d'une étape. La décision est dans le
    /// noyau, où elle s'éprouve ; il ne reste ici que le calcul du rang.
    /// </summary>
    private string StepLabel(int index) =>
        QuestStepLabel.For(_steps[index], IsDeparture(index), _start);

    /// <summary>
    /// Le rang tel qu'il se lit : « Étape 2 / 5 », ou « Départ » pour le
    /// lancement, qui n'est pas une étape du parcours et ne se compte donc pas.
    /// </summary>
    private string StepRank(int index) =>
        QuestStepLabel.Numbering(index, _steps.Count, HasDeparture) is { } numbering
            ? Strings.Format("StepOfTotal", numbering.Rank, numbering.Total)
            : Strings.Get("StepStart");

    /// <summary>
    /// Le seul numéro, pour la colonne étroite de la liste. Le départ n'en a
    /// pas : sa ligne se reconnaît à ce qu'elle dit, « Rendez-vous en… », et
    /// écrire « Départ » dans vingt-six pixels était impossible.
    /// </summary>
    private string StepNumber(int index) =>
        QuestStepLabel.Numbering(index, _steps.Count, HasDeparture) is { } numbering
            ? numbering.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>Vrai quand la première étape rendue est le lancement.</summary>
    private bool HasDeparture => _steps.Count > 0 && IsDeparture(0);

    private bool IsDeparture(int index) =>
        QuestStepLabel.IsDeparture(_steps[index], index == 0, _startsAtDeparture);

    /// <summary>
    /// Vrai quand il y a de quoi choisir : à partir de deux étapes.
    ///
    /// Sur un guide d'une seule étape, la pastille dépliait une liste d'un
    /// élément, ce qui ne menait nulle part.
    /// </summary>
    public bool CanPickStep => _steps.Count > 1;

    /// <summary>
    /// Repose les étapes, et la liste où on les choisit avec elles.
    ///
    /// Une seule porte pour les deux : la liste et le compte se contredisaient
    /// dès qu'un chemin oubliait l'une des deux lignes.
    /// </summary>
    private void ResetSteps(IReadOnlyList<QuestStep> steps)
    {
        _steps = steps;
        HasSteps = steps.Count > 0;
        OnPropertyChanged(nameof(CanPickStep));

        Steps.Clear();

        for (var index = 0; index < steps.Count; index++)
        {
            Steps.Add(new QuestStepRowViewModel(index, StepNumber(index), StepLabel(index)));
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
        //
        // Les trois champs d'état sont vidés : ils désignaient encore la page
        // d'avant, et tout ce qui s'en sert mentait donc. La liste rouvrait sur
        // la rubrique de l'ancienne quête, et le signalement la nommait à la
        // place de celle qu'on lisait.
        _current = null;
        _currentDungeon = null;
        _currentPath = null;

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

        // Un donjon, un raid, une tanière et un chemin se suivent comme une
        // quête : ce sont des pages du site que le catalogue connaît. Sans cela,
        // le lien d'un guide vers l'une d'elles partait dans une fenêtre à part.
        var quest = _catalog.Catalog.Quests.FirstOrDefault(q =>
            string.Equals(UrlKey(q.Url), key, StringComparison.Ordinal));

        var dungeon = quest is null
            ? _catalog.Catalog.Dungeons.FirstOrDefault(d =>
                string.Equals(UrlKey(d.Url), key, StringComparison.Ordinal))
            : null;

        var path = quest is null && dungeon is null
            ? _catalog.Catalog.Paths.FirstOrDefault(p =>
                string.Equals(UrlKey(p.Url), key, StringComparison.Ordinal))
            : null;

        if (quest is null && dungeon is null && path is null)
        {
            return false;
        }

        // L'empilement se fait ici, avant l'aiguillage, et vaut donc pour les
        // quatre natures. Il ne se faisait que dans la branche des quêtes : un
        // lien vers un chemin ou un donjon n'entrait jamais dans l'historique,
        // et la flèche de retour ne paraissait pas.
        //
        // Seule une page que le catalogue sait rouvrir est empilée : le retour
        // repasse par cette même porte, et une adresse qu'elle refuserait
        // laisserait la flèche sans effet.
        if (remember
            && CurrentUrl is { Length: > 0 } quittee
            && (_current is not null || _currentDungeon is not null || _currentPath is not null)
            && !string.Equals(UrlKey(quittee), key, StringComparison.Ordinal))
        {
            _visited.Push(quittee);
            CanGoBackPage = true;
        }

        // Un lien suivi dans le guide ne déplace pas le repère de la liste : on
        // va voir un chemin, et l'on veut retrouver la quête en rouvrant le
        // panneau. Tout le reste, choix dans la liste ou bouton du pied, le
        // déplace.
        var anchor = !remember;

        if (dungeon is not null)
        {
            SetCurrent(dungeon, anchor);
            IsListOpen = false;

            return true;
        }

        if (path is not null)
        {
            SetCurrent(path, anchor);
            IsListOpen = false;

            return true;
        }

        SetCurrent(quest!, anchor);
        IsListOpen = false;

        return true;
    }

    /// <summary>
    /// Adresse réduite à ce qui l'identifie.
    ///
    /// Le site écrit ses liens tantôt avec la barre finale, tantôt sans, et la
    /// comparaison stricte manquait alors une quête pourtant au catalogue.
    /// </summary>
    private static string UrlKey(string? url)
    {
        var text = (url ?? string.Empty).Trim();

        // Le fragment part : le site renvoie parfois vers une ancre d'une page
        // qu'il connaît, « …/quete-x/#etape-3 », et la comparaison stricte la
        // prenait pour une page inconnue qui partait en fenêtre annexe. La
        // chaîne de requête, elle, reste : au moins une adresse du catalogue en
        // fait son identité.
        var anchor = text.IndexOf('#', StringComparison.Ordinal);

        if (anchor >= 0)
        {
            text = text[..anchor];
        }

        return text.TrimEnd('/').ToLowerInvariant();
    }

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
            exception.Message + Strings.Get("BrowserFallback");
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
    /// <summary>
    /// Ouvre la recherche du site dans le navigateur.
    ///
    /// Dans le vrai navigateur et non dans nos fenêtres : une page de résultats
    /// n'est pas un guide, elle n'a ni étapes ni chaîne, et notre cadrage n'y
    /// laisserait qu'une colonne de liens sans en-tête.
    /// </summary>
    public void OpenSiteSearch()
    {
        if (PapychaSite.SearchUrl(SiteSearch) is { } url)
        {
            _dialogs.OpenUrl(url);
        }
    }

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
    /// la page, et le repère à porter dans le formulaire du site.
    ///
    /// Le repère est la zone, la quête et son succès, c'est-à-dire ce que le site
    /// nomme lui-même. Le succès vient de la quête et non de <c>ChainText</c>,
    /// que le bandeau détourne pour dire « Chemin » sur un chemin et la clef sur
    /// un donjon. Le rang de l'étape y figurait d'abord ; c'est une numérotation
    /// qui n'existe que dans cette fenêtre, et elle ne désignait donc rien pour
    /// qui reçoit le signalement.
    /// </summary>
    public (string Url, string Location)? ErrorReport() =>
        CanReport
            ? (CurrentUrl!, PapychaReport.Location(ZoneName(), QuestTitle, _current?.SuccessName))
            : null;

    /// <summary>
    /// Le nom de la rubrique sous laquelle on lit, ou vide quand la page n'en a
    /// pas. La même règle que la liste : celle qu'on parcourt si la quête y
    /// figure, sinon celle que le catalogue lui a retenue.
    /// </summary>
    private string ZoneName() =>
        SectionOfPage() is { } id ? _tree.NameOf(id) : string.Empty;

    /// <summary>
    /// La rubrique de la page qu'on lit, quelle qu'en soit la nature.
    ///
    /// Les trois cas vivaient en double, ici et dans le repère de la liste, et
    /// ils avaient divergé : le signalement ignorait les chemins et n'en
    /// nommait aucune rubrique. Un seul endroit, désormais.
    /// </summary>
    private int? SectionOfPage() =>
        (_current is { } lue ? SectionSeen(lue) : (int?)null)
        ?? (_currentDungeon is { } place ? QuestTree.SectionOf(place) : (int?)null)
        ?? (_currentPath is { } road
            ? road.Side == PathSide.Dungeons ? QuestTree.DungeonPathSection : QuestTree.QuestPathSection
            : (int?)null);

    /// <summary>
    /// Vrai quand il y a un formulaire à ouvrir, c'est-à-dire quand un guide
    /// est sous les yeux.
    ///
    /// Le site ne met de formulaire de signalement qu'en pied d'article, mesuré
    /// page par page : les rubriques n'en ont pas, et il n'en existe pas de
    /// général. Le bouton disparaît donc plutôt que de mener nulle part.
    /// </summary>
    public bool CanReport => !IsListOpen && HasQuest && PapychaSite.Owns(CurrentUrl);

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

        if (_section == QuestTree.RootSection)
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

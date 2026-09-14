using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.App.ViewModels;

/// <summary>
/// What the quest window shows: the search, its results, and the open quest.
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
    /// What the dropdown list shows at this moment: the branches, the quests
    /// of a section, or the results of a search.
    /// </summary>
    public ObservableCollection<QuestNode> Nodes { get; } = [];

    /// <summary>Number of catalogue quests filed under each section.</summary>
    private readonly Dictionary<int, int> _sectionCounts = [];

    /// <summary>
    /// The branches, built from the catalogue and this tally.
    /// </summary>
    private readonly QuestTree _tree;

    /// <summary>How many steps the open page announces.</summary>
    public int StepCount => _steps.Count;

    /// <summary>Places in the list the rows the tree builds.</summary>
    private void AddBySuccess(IReadOnlyList<QuestSummary> quests)
    {
        foreach (var node in _tree.BySuccess(quests))
        {
            Nodes.Add(node);
        }
    }

    /// <summary>Open section, or zero at the root.</summary>
    private int _section;

    /// <summary>The displayed quest, when there is one.</summary>
    private QuestSummary? _current;

    /// <summary>The displayed dungeon, when it is one.</summary>
    private DungeonSummary? _currentDungeon;

    /// <summary>The displayed path, when it is one.</summary>
    private PathSummary? _currentPath;

    /// <summary>
    /// The quests left behind by following a link, the most recent on top.
    ///
    /// Only links count: those in the guide and those in the prerequisites,
    /// which lead elsewhere without having been sought, and from which nothing
    /// brings you back. A neighbour picked at the foot, or a quest picked from
    /// the list, does not belong here: we know where we came from when it is
    /// ourselves who chose where to go.
    /// </summary>
    private readonly Stack<string> _visited = new();

    /// <summary>
    /// What the list rests on: the section and the address of the last page we
    /// designated ourselves, from the list or from the footer buttons.
    ///
    /// A link followed in the guide does not move them: we go to look at a
    /// path or a dungeon, and we want to get back to the page we came from
    /// when reopening the panel. Without this marker, opening a path from a
    /// quest reopened the list on the paths branch, and nothing said any more
    /// where we had come from.
    /// </summary>
    private int? _anchorSection;

    private string? _anchorUrl;

    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>
    /// The text for which the site search is offered, empty when nothing is
    /// being searched.
    ///
    /// Our catalogue knows only titles. Searching for an item, a monster or a
    /// character gives nothing there, whereas the site finds it: it searches
    /// inside the body of its articles. The offer therefore follows the
    /// search, from the first letter: it first appeared on a line break, and
    /// the screen that needed it most, the one announcing "No result", was
    /// precisely the one that did not have it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SiteSearchLabel))]
    [NotifyPropertyChangedFor(nameof(ShowsSiteSearch))]
    private string _siteSearch = string.Empty;

    /// <summary>What the link announces, the searched text included.</summary>
    public string SiteSearchLabel =>
        SiteSearch.Length == 0 ? string.Empty : Strings.Format("SearchOnSite", SiteSearch);

    /// <summary>
    /// True when the offer to search the site makes sense: a searched text,
    /// and the list in view.
    ///
    /// **It used to survive the guide we had just opened.** The searched text
    /// is cleared only when going down into a section; opening a quest from a
    /// result leaves it in place, and the footer therefore kept offering
    /// "Search … on papycha.fr" at the bottom of a guide already open, even
    /// though the search was finished and had succeeded.
    ///
    /// **The first fix aimed wide.** It required that no quest be open, and so
    /// made the offer disappear as soon as we searched from within a guide,
    /// which is the most common gesture: we read, we want something else, we
    /// type. The question is not whether a page is open but whether the list
    /// is on screen.
    ///
    /// <see cref="IsListOpen" /> answers exactly this, and typing
    /// text sets it. The offer therefore appears on the results screen, and
    /// especially on the one announcing "No result": that is where we want to
    /// go look elsewhere. In front of a guide alone, it offers nothing that
    /// the reader is searching for.
    /// </summary>
    public bool ShowsSiteSearch => SiteSearch.Length > 0 && IsListOpen;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _questTitle = string.Empty;

    [ObservableProperty]
    private string _chainText = string.Empty;

    /// <summary>
    /// Position of the quest in its prerequisite chain, "6 / 7". Shown in the
    /// footer, between the previous and the next: it is this chain that it
    /// talks about, not the achievement.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    private string _chainStep = string.Empty;

    /// <summary>True when a page is open in the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsQuestChrome))]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    [NotifyPropertyChangedFor(nameof(CanReport))]
    [NotifyPropertyChangedFor(nameof(CanCloseList))]
    private bool _hasQuest;

    /// <summary>What is shown as long as no quest is open.</summary>
    [ObservableProperty]
    private string _placeholder = Strings.Get("SearchPlaceholder");

    /// <summary>
    /// Row to highlight when the panel opens: the one for the displayed quest.
    /// The selection used to be set only by the down arrow from the search, so
    /// reopening the list highlighted a quest we had left long before.
    /// </summary>
    [ObservableProperty]
    private QuestNode? _selectedNode;

    /// <summary>True when the dropdown list is open.</summary>
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
    /// True for as long as it takes a guide page to arrive.
    ///
    /// The banner announces the new quest as soon as we click, but the view
    /// still shows the previous guide for a second or two: we would think the
    /// click had done nothing, or worse, we would be reading the wrong page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    [NotifyPropertyChangedFor(nameof(ShowsLoader))]
    [NotifyPropertyChangedFor(nameof(ShowsSteps))]
    private bool _isLoadingPage;

    /// <summary>
    /// True when the window is on screen.
    ///
    /// It is only used to remove the web view when the window is hidden, which
    /// is the condition for putting the rendering engine to sleep: it refuses
    /// to sleep as long as it believes itself visible, and says so with a
    /// state error.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    private bool _isWindowVisible = true;

    /// <summary>True during indexing, to show that it is working.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// True when the step banner should be visible.
    ///
    /// It disappears while the list is open: the list then takes up the whole
    /// height, and we do not consult a step and a list at the same time.
    /// </summary>
    public bool ShowsQuestChrome => HasQuest && !IsListOpen;

    /// <summary>
    /// True if closing the list leads somewhere.
    ///
    /// The cross gives back the place to the guide we were reading. With no
    /// guide open, it gives back nothing: it trades a usable list for an empty
    /// window that invites us to reopen that same list. A command whose only
    /// effect is to undo what we just did should not be offered.
    /// </summary>
    public bool CanCloseList => IsListOpen && HasQuest;

    /// <summary>
    /// True when the achievement footer has something to say.
    ///
    /// A dungeon, a raid, a lair and a path belong to no sequence: they have
    /// no quest before, no quest after, no rank in an achievement, and the
    /// footer used to show for them only a thin line and an empty bar.
    /// </summary>
    public bool ShowsChain =>
        ShowsQuestChrome
        && (PreviousQuest is not null || NextQuest is not null || ChainStep.Length > 0);

    /// <summary>
    /// True when the web view should be visible. It is removed during loading,
    /// not covered: a native window draws itself above every WPF element in
    /// the same shell, so a veil placed over it would stay invisible. This is,
    /// moreover, what the dropdown list already does.
    /// </summary>
    public bool ShowsPage => !IsListOpen && !IsLoadingPage && IsWindowVisible;

    /// <summary>
    /// True when the view's place is taken over by the loading indicator.
    /// </summary>
    public bool ShowsLoader => IsLoadingPage && !IsListOpen;

    /// <summary>
    /// True when the step row should be visible.
    ///
    /// It holds its place during loading, when the steps are not yet known:
    /// without this the banner used to lose a row and then get it back,
    /// jumping at every quest change. What it shows then is the start, which
    /// comes from the metadata and does not wait for the page.
    /// </summary>
    public bool ShowsSteps => HasSteps || IsLoadingPage;

    /// <summary>Where we stand in the tree, shown above the list.</summary>
    [ObservableProperty]
    private string _breadcrumb = string.Empty;

    /// <summary>
    /// Address of the open page, to reopen it in the browser.
    /// </summary>
    public string? CurrentUrl { get; private set; }

    /// <summary>
    /// Prepares the catalogue. The window stays usable during indexing: it
    /// takes a few seconds the first time, and nothing afterwards.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var before = QuestTally.Of(_catalog.Catalog);

        IsBusy = true;

        // **Yesterday's tree is better than an empty tree.** The cached
        // catalogue is already loaded in memory by the time we get here, and
        // yet the window used to show "Quests 0" throughout the whole reread:
        // it was waiting for a network read to finish before showing what it
        // already had on disk. We show it right away, with the status line
        // saying elsewhere that an update is in progress.
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
    /// Duration of the last full indexing, or <c>null</c> if there has not
    /// been one. Logged by the window, since the view model has no logger.
    /// </summary>
    public TimeSpan? LastIndexing => _catalog.LastIndexing;

    /// <summary>
    /// Settles what follows a read: the chain and the displayed status.
    /// </summary>
    private void Settle(QuestCatalogDocument catalog, QuestTally before)
    {
        _tree.Chain = new QuestChainIndex(catalog.Quests);

        IsBusy = false;

        // What the reread brought back, when there was something before to
        // compare it with. The first indexing stays silent: announcing "seven
        // hundred and eighty-two quests more" would teach nothing.
        //
        // This is said without being asked, because nobody asks for a reread:
        // the sentinel decides, and we want to know what it found.
        StatusText = catalog.Quests.Count == 0
            ? Strings.Get("NoQuestSiteDown")
            : _catalog.LastFailure is not null
                ? Strings.Get("SiteDownCached")
                : QuestTally.Of(catalog).Since(before);
    }

    /// <summary>
    /// Counts the quests by section from the catalogue, not from the site's
    /// own numbers: the site also counts what is not a quest, and would offer
    /// sections that opened onto nothing.
    ///
    /// On every section the quest belongs to, the way the list shows them: the
    /// site files "Le dragon d'Astrub" under its main quests as well as under
    /// those of Astrub.
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
        // The offer follows the typed text, word for word: it is the same
        // thing as what we are searching for, so it cannot depart from it.
        SiteSearch = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            // Clearing the search brings us back to where we were, rather than
            // to the root: we often clear it to fix a typo.
            ShowSection(_section);
            return;
        }

        ShowSearch(value);

        // Searching without seeing the results makes no sense: until now,
        // typing text rebuilt the list without unrolling it.
        IsListOpen = true;
    }

    /// <summary>Opens the list on what was displayed.</summary>
    public void OpenList()
    {
        if (Nodes.Count == 0)
        {
            ShowRoot();
        }

        // The list reopens on the section of the displayed quest, unless it is
        // already there: it then keeps its achievements unrolled and what had
        // been expanded, and we only find again where we stand.
        //
        // Without this, it used to reopen on "Quests / Dungeons" or on the
        // results of a search while a guide was displayed, and we had to climb
        // back down the tree to find the neighbours of what we were reading. A
        // dungeon belongs to no section of the site: its branch is its own.
        var section = _anchorSection ?? SectionOfPage();

        if (section is { } target && (_section != target || Query.Length > 0))
        {
            Query = string.Empty;
            ShowSection(target);
        }

        SelectCurrent();

        IsListOpen = true;
    }

    /// <summary>
    /// Sets the list's marker on the page we just designated.
    /// </summary>
    private void Anchor(int section, string url)
    {
        _anchorSection = section;
        _anchorUrl = url;
    }

    /// <summary>
    /// The section under which a quest is read: the one we are browsing when
    /// it belongs to it, otherwise the one the catalogue has kept for it.
    ///
    /// A quest can belong to several sections, and the catalogue keeps only
    /// one in <c>SectionId</c>, the least populated one. Reopening the list
    /// from a repeatable quest read in Frigost therefore used to switch to
    /// "Quêtes répétables", even though we had come from Frigost. Measured on
    /// the catalogue: 215 quests out of 782 belong to more than one section,
    /// of which 93 are claimed by a cross-cutting section and 80 go the other
    /// way.
    /// </summary>
    private int SectionSeen(QuestSummary quest) =>
        quest.SectionIds.Contains(_section) ? _section : quest.SectionId;

    /// <summary>
    /// Sets the selection on the displayed quest, if it is in the list.
    /// Touches nothing when it is not there: the list may be showing another
    /// section, and clearing it would be worse than not highlighting anything.
    /// </summary>
    public void SelectCurrent()
    {
        // The marker rather than the current page: after following a link, it
        // is the page we came from that we want to find highlighted again.
        var vise = _anchorUrl ?? CurrentUrl;

        if (string.IsNullOrEmpty(vise))
        {
            return;
        }

        SelectedNode = Nodes.FirstOrDefault(n =>
            n.Url is { } url && string.Equals(url, vise, StringComparison.Ordinal));
    }

    /// <summary>The first level: the main branches.</summary>
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
    /// The content of a section. At the quests root, these are the other
    /// sections; further down, these are the quests themselves.
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

            // With no tiers: ten rows are read in one go, and splitting them
            // would help no one.
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
    /// Section the back button leads back to, when there is a level above.
    ///
    /// The back button used to be a row of the list like any other: it
    /// scrolled along with it and disappeared as soon as we went down into a
    /// section of sixty quests. It is now fixed, above the list.
    /// </summary>
    [ObservableProperty]
    private bool _canGoBack;

    /// <summary>
    /// True when the history of read pages has somewhere to go back to.
    /// </summary>
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
    /// Goes back to the quest we came from, and returns its address for the
    /// window to load. Returns <c>null</c> when the history is empty.
    ///
    /// Going back does not push itself onto the stack: only a link followed in
    /// place does, otherwise the arrow would shuttle back and forth between
    /// two quests.
    /// </summary>
    public string? GoBackPage()
    {
        if (_visited.Count == 0)
        {
            return null;
        }

        var url = _visited.Pop();

        CanGoBackPage = _visited.Count > 0;

        // Through the same door as the forward trip: only addresses the
        // catalogue knows how to reopen are pushed onto the stack, and it is
        // the catalogue that decides the nature.
        TryFollowUrl(url);

        IsListOpen = false;

        return url;
    }

    /// <summary>Goes back up one level.</summary>
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
    /// Results of a search. It covers every quest, whatever section is open:
    /// we are searching for a name, not a filing.
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

            // An achievement is not something you pick: what is wanted is its
            // quests. They therefore follow its name, in the order in which
            // they are played.
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

        // One group per kind, and only if it found something: an ordinary
        // search shows one or two of them.
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
    /// The three groups of combat locations, in the order of the root. The
    /// four texts are translation keys and not labels: the table is static,
    /// and the language is known only at display time.
    /// </summary>


    /// <summary>What separates two levels of a breadcrumb trail.</summary>
    private const string Separator = QuestTree.Separator;


    /// <summary>
    /// Follows up on a click in the list. Returns the address to load, or null
    /// when the click only unrolls a branch.
    ///
    /// An address and not a quest: the list can just as well lead to a
    /// dungeon, and the window only needs to know what to open.
    /// </summary>
    public string? Activate(QuestNode? node)
    {
        if (node is null || !node.IsEnabled)
        {
            return null;
        }

        // Picking from the list clears the trail: we have designated where to
        // go, and what we were reading before no longer means anything.
        // Otherwise the arrow stayed lit, pointing to an unrelated page.
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

    /// <summary>
    /// Remembers the open quest, for the window footer and the title.
    /// </summary>
    /// <param name="anchor">
    /// False when following a link in the guide: the page is displayed, but
    /// the list's marker stays on the page we came from.
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

        // The next page is not loaded yet: keeping the previous one's steps
        // would show an objective that no longer has anything to do with it.
        // The start, however, is already known and holds the row in the
        // meantime.
        ResetSteps([]);
        SetStep(-1);

        StepDetail = _start ?? string.Empty;
    }

    /// <summary>
    /// Opens a dungeon.
    ///
    /// It has neither an achievement nor neighbours: dungeons are not chained
    /// together like the quests of a series, we pick one. The banner therefore
    /// carries only its name and its start, which the site gives in its
    /// metadata as it does for a quest.
    /// </summary>
    /// <param name="anchor">See the quests overload.</param>
    public void SetCurrent(DungeonSummary dungeon, bool anchor = true)
    {
        ArgumentNullException.ThrowIfNull(dungeon);

        _current = null;
        _currentDungeon = dungeon;
        _currentPath = null;
        _neighbours = default;

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
    /// Opens a path.
    ///
    /// It has neither a level nor neighbours, and no start to compose: an
    /// itinerary begins wherever we are. The banner therefore carries only its
    /// name, and the steps will come from its section titles.
    /// </summary>
    /// <param name="anchor">See the quests overload.</param>
    public void SetCurrent(PathSummary path, bool anchor = true)
    {
        ArgumentNullException.ThrowIfNull(path);

        _current = null;
        _currentDungeon = null;
        _currentPath = path;
        _neighbours = default;

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
    /// Establishes the achievement of the open quest, its rank and its
    /// neighbours.
    ///
    /// The choice of neighbours is in the core,
    /// <see cref="QuestNeighbourhood"/> ; only the dressing is left here,
    /// which requires knowing which section we are browsing. These are the
    /// neighbours the catalogue knows: the page, once it arrives, will say
    /// what the site publishes and will take precedence.
    /// </summary>
    /// <summary>
    /// What the catalogue decided for the open quest. Kept because the page,
    /// on arriving, needs to know whether the achievement's list had already
    /// settled it.
    /// </summary>
    private QuestNeighbours _neighbours;

    private void SetNeighbours(QuestSummary quest)
    {
        var neighbours = QuestNeighbourhood.Of(quest, _catalog.Catalog.Quests, _tree.Chain);

        _neighbours = neighbours;

        ChainText = quest.SuccessName;

        ShowProgress();

        PreviousQuest = ToLink(neighbours.Previous, quest);
        NextQuest = ToLink(neighbours.Next, quest);
    }

    /// <summary>
    /// Writes the rank and the total at the foot of the window.
    ///
    /// The rule lives in <see cref="QuestProgress"/>, which says why it
    /// counts the achievement's quests and not what the page publishes.
    /// </summary>
    private void ShowProgress()
    {
        var shown = QuestProgress.Of(_neighbours);

        ChainStep = $"{QuestTree.Text(shown.Rank)} / {QuestTree.Text(shown.Total)}";
    }

    /// <summary>
    /// The link to a neighbouring quest, announced by its series when we
    /// change series.
    ///
    /// Following a prerequisite sometimes takes us into another achievement,
    /// or even another area. The title alone would suggest that we are
    /// continuing the same sequence; the name of the achievement (or, failing
    /// that, that of the area) tells us that we are starting a different one.
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
            // By the section we are browsing, not the one the catalogue kept:
            // two quests from the same area used to be announced as being from
            // a different series because one of them is also repeatable.
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
    /// The neighbour the site names, rendered with its series label when the
    /// catalogue knows it.
    ///
    /// It knows it almost always, and the label is what warns that we are
    /// changing achievement or area. When it does not know it, we keep the
    /// site's title and address rather than silencing the sequence: the button
    /// then leads to a page the window will open on its own.
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

    /// <summary>Steps found in the open page.</summary>
    private IReadOnlyList<QuestStep> _steps = [];

    /// <summary>
    /// The guide's steps, as picked from the list the rank unrolls. The two
    /// arrows only move one at a time.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<QuestStepRowViewModel> Steps { get; } = [];

    /// <summary>
    /// Shortcut for the first step, composed from the metadata of the open
    /// quest. Null when the site says neither where nor with whom it is
    /// started.
    /// </summary>
    private string? _start;

    /// <summary>
    /// True when the first step is the quest's start and not a paragraph of
    /// the guide. The bridge announces it, because only it can see whether the
    /// page carries a start block.
    /// </summary>
    private bool _startsAtDeparture;

    [ObservableProperty]
    private int _stepIndex = -1;

    /// <summary>"Step 3 / 7", or nothing when the page has no step.</summary>
    [ObservableProperty]
    private string _stepText = string.Empty;

    /// <summary>What there is to do at this step, in one line.</summary>
    [ObservableProperty]
    private string _stepDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSteps))]
    private bool _hasSteps;

    [ObservableProperty]
    private bool _canGoPreviousStep;

    [ObservableProperty]
    private bool _canGoNextStep;

    /// <summary>
    /// Next quest of the achievement, if there is one after this one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    private QuestLink? _nextQuest;

    /// <summary>
    /// Previous quest of the achievement, if there is one before this one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChain))]
    private QuestLink? _previousQuest;

    /// <summary>
    /// What the page has just delivered: its structured blocks and its steps.
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

        // The site publishes at the foot of the article what comes before and
        // what comes after, and it is the site that is authoritative **where
        // we have nothing**: at the edges of an achievement and for quests
        // that have none. SetCurrent has set our order before the page
        // arrives, so that the buttons respond during loading; the page
        // completes it on arrival.
        //
        // But it no longer replaces it inside a list. This column is titled
        // "Quêtes et jalons suivants": it says what the quest unlocks, not the
        // order in which an achievement is read. In "Le théâtre des gobelins",
        // the one for "Titi Gobelait le magobelin" names only "Manque de
        // moule", which requires it, and following the column skipped "Un
        // avenir de krotte de Trooll", which comes before and requires
        // nothing.
        //
        // And never when the column names several quests: naming just one
        // would lie.
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

        // The achievement's name comes from the catalogue, so that the list
        // and the page agree. We fall back to the page's own name only for a
        // quest the catalogue attaches to nothing.
        if (ChainText.Length == 0)
        {
            ChainText = facts.Success ?? string.Empty;
        }

        // A dungeon, a raid, a lair and a path belong to no sequence:
        // the count at the foot of the window is a quest's business.
        // A quest the catalogue does not know reaches this point with
        // no neighbours, and reads one of one.
        if (_currentDungeon is null && _currentPath is null)
        {
            ShowProgress();
        }

        ResetSteps(steps);

        SetStep(steps.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// Scrolling has changed step, or the user has picked one.
    /// </summary>
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
    /// What is written next to a step's rank. The decision is in the core,
    /// where it is put to the test; only the rank's computation is left here.
    /// </summary>
    private string StepLabel(int index) =>
        QuestStepLabel.For(_steps[index], IsDeparture(index), _start);

    /// <summary>
    /// The rank as it reads: "Step 2 / 5", or "Start" for the launch, which is
    /// not a step of the journey and so is not counted.
    /// </summary>
    private string StepRank(int index) =>
        QuestStepLabel.Numbering(index, _steps.Count, HasDeparture) is { } numbering
            ? Strings.Format("StepOfTotal", numbering.Rank, numbering.Total)
            : Strings.Get("StepStart");

    /// <summary>
    /// The number alone, for the list's narrow column. The start has none: its
    /// row is recognisable by what it says, "Rendez-vous en…", and writing
    /// "Start" in twenty-six pixels was impossible.
    /// </summary>
    private string StepNumber(int index) =>
        QuestStepLabel.Numbering(index, _steps.Count, HasDeparture) is { } numbering
            ? numbering.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>True when the first rendered step is the launch.</summary>
    private bool HasDeparture => _steps.Count > 0 && IsDeparture(0);

    private bool IsDeparture(int index) =>
        QuestStepLabel.IsDeparture(_steps[index], index == 0, _startsAtDeparture);

    /// <summary>
    /// True when there is something to choose from: starting at two steps.
    ///
    /// On a guide with a single step, the badge used to unroll a list of one
    /// item, which led nowhere.
    /// </summary>
    public bool CanPickStep => _steps.Count > 1;

    /// <summary>
    /// Resets the steps, and the list where they are picked, together.
    ///
    /// A single door for both: the list and the count used to contradict each
    /// other as soon as one code path forgot one of the two lines.
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
    /// We follow an achievement link. The title is picked up right away: the
    /// page takes a second to respond, and a banner that keeps the old name
    /// during that time gives the impression the click did nothing.
    ///
    /// The quest is found again in the catalogue by its address, so that the
    /// footer stays populated. It used not to be: the method cleared the
    /// previous and next quests without ever restoring them, so that after a
    /// single jump the navigation went dead and we had to go back through the
    /// list.
    /// </summary>
    public void Follow(QuestLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (TryFollowUrl(link.Url))
        {
            return;
        }

        // An address the catalogue does not know: we open it anyway, with no
        // neighbours, rather than doing nothing.
        //
        // The four state fields are cleared: they still designated the
        // previous page, so everything that used them was therefore lying. The
        // list used to reopen on the section of the old quest, and the report
        // form named it instead of the one we were reading. The neighbours
        // were in the same case, and were the last to be let through: the
        // page's own sequence was weighed against the rank of a quest we had
        // already left.
        _current = null;
        _currentDungeon = null;
        _currentPath = null;
        _neighbours = default;

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
    /// Follows an address in place when the catalogue knows it, and says
    /// whether it did.
    ///
    /// A guide links to its neighbouring quests through plain links: clicking
    /// one used to open a second window, whereas the "previous" button, which
    /// leads to the same place, stayed in place. The catalogue settles it:
    /// what it knows is followed here, the rest goes elsewhere.
    /// </summary>
    public bool TryFollowUrl(string? url, bool remember = false)
    {
        var key = UrlKey(url);

        if (key.Length == 0)
        {
            return false;
        }

        // A dungeon, a raid, a lair and a path are followed just like a quest:
        // they are pages of the site the catalogue knows. Without this, a
        // guide's link to one of them used to open in a separate window.
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

        // The push onto the stack happens here, before the routing, and
        // therefore applies to all four kinds. It used to happen only in the
        // quests branch: a link to a path or a dungeon never entered the
        // history, and the back arrow did not appear.
        //
        // Only a page the catalogue knows how to reopen is pushed: going back
        // passes through this same door again, and an address it would refuse
        // would leave the arrow with no effect.
        if (remember
            && CurrentUrl is { Length: > 0 } quittee
            && (_current is not null || _currentDungeon is not null || _currentPath is not null)
            && !string.Equals(UrlKey(quittee), key, StringComparison.Ordinal))
        {
            _visited.Push(quittee);
            CanGoBackPage = true;
        }

        // A link followed in the guide does not move the list's marker: we go
        // to look at a path, and we want to find the quest again by reopening
        // the panel. Everything else, a pick from the list or a footer button,
        // moves it.
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
    /// Address reduced to what identifies it.
    ///
    /// The site writes its links sometimes with a trailing slash, sometimes
    /// without, and a strict comparison used to miss a quest that was
    /// nonetheless in the catalogue.
    /// </summary>
    private static string UrlKey(string? url)
    {
        var text = (url ?? string.Empty).Trim();

        // The fragment is dropped: the site sometimes points to an anchor on a
        // page it knows, "…/quete-x/#etape-3", and a strict comparison used to
        // mistake it for an unknown page that opened in a side window. The
        // query string, however, stays: at least one address in the catalogue
        // makes it part of its identity.
        var anchor = text.IndexOf('#', StringComparison.Ordinal);

        if (anchor >= 0)
        {
            text = text[..anchor];
        }

        return text.TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>
    /// Rank of the first row that can be picked, stepping over the
    /// subheadings. Returns -1 if the list offers nothing.
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

    /// <summary>
    /// Step targeted by an arrow, or -1 if there is nowhere to go.
    /// </summary>
    public int StepTarget(int direction)
    {
        var target = StepIndex + direction;

        return target >= 0 && target < _steps.Count ? target : -1;
    }

    /// <summary>
    /// The embedded browser could not start up.
    ///
    /// The incident's message already carries its cause when it is known: the
    /// environment itself says that the WebView2 component is missing, and
    /// where to get it. We only add the fallback, which holds in every case.
    /// </summary>
    public void ReportViewFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        HasQuest = false;
        IsLoadingPage = false;
        Placeholder =
            exception.Message + Strings.Get("BrowserFallback");
    }

    /// <summary>The site's home page.</summary>
    private const string HomeUrl = "https://papycha.fr/";

    /// <summary>The site page that lists the quest areas.</summary>
    private const string IndexUrl = "https://papycha.fr/quetes/";

    /// <summary>
    /// Reopens on the site what the window shows, not the quest in every
    /// circumstance.
    ///
    /// The button always used to lead to the current quest, including when the
    /// list covered the screen on a section being browsed: it then opened
    /// something other than what was in front of us, or nothing at all as long
    /// as no quest had been picked.
    ///
    /// A search is an exception: it shows no section, and what we were reading
    /// before launching it remains the quest.
    /// </summary>
    /// <summary>
    /// Opens the site's search in the browser.
    ///
    /// In the real browser and not in our windows: a results page is not a
    /// guide, it has neither steps nor a chain, and our frame would leave it
    /// as nothing but a column of links with no header.
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
    /// What is needed to report an error on what is in front of us: the page,
    /// and the marker to carry into the site's form.
    ///
    /// The marker is the area, the quest and its achievement, that is to say
    /// what the site itself names. The achievement comes from the quest and
    /// not from <c>ChainText</c>, which the banner diverts to say "Path" on a
    /// path and the key on a dungeon. The step's rank used to appear there
    /// first; that is a numbering that exists only in this window, and so it
    /// designated nothing to whoever receives the report.
    /// </summary>
    public (string Url, string Location)? ErrorReport() =>
        CanReport
            ? (CurrentUrl!, PapychaReport.Location(ZoneName(), QuestTitle, _current?.SuccessName))
            : null;

    /// <summary>
    /// The name of the section we are reading under, or empty when the page
    /// has none. The same rule as the list: the one being browsed if the quest
    /// is in it, otherwise the one the catalogue has kept for it.
    /// </summary>
    private string ZoneName() =>
        SectionOfPage() is { } id ? _tree.NameOf(id) : string.Empty;

    /// <summary>
    /// The section of the page we are reading, whatever its kind.
    ///
    /// The three cases used to live in duplicate, here and in the list's
    /// marker, and they had diverged: the report used to ignore paths and
    /// named no section for them. A single place now, for both.
    /// </summary>
    private int? SectionOfPage() =>
        (_current is { } lue ? SectionSeen(lue) : (int?)null)
        ?? (_currentDungeon is { } place ? QuestTree.SectionOf(place) : (int?)null)
        ?? (_currentPath is { } road
            ? road.Side == PathSide.Dungeons ? QuestTree.DungeonPathSection : QuestTree.QuestPathSection
            : (int?)null);

    /// <summary>
    /// True when there is a form to open, meaning when a guide is in front of
    /// us.
    ///
    /// The site puts a report form only at the foot of an article, measured
    /// page by page: sections have none, and there is no general one. The
    /// button therefore disappears rather than leading nowhere.
    /// </summary>
    public bool CanReport => !IsListOpen && HasQuest && PapychaSite.Owns(CurrentUrl);

    /// <summary>
    /// The page of what the list is browsing, or <c>null</c> if it is browsing
    /// nothing: list closed, search in progress, or section with no page
    /// written.
    /// </summary>
    private string? Browsed()
    {
        if (!IsListOpen || Query.Length > 0)
        {
            return null;
        }

        // The root of the menu is not the list of areas: it announces quests
        // and dungeons, that is everything the site offers. So it is the
        // site's home page that it opens, with the areas page one level down.
        if (_section == 0)
        {
            return HomeUrl;
        }

        if (_section == QuestTree.RootSection)
        {
            return IndexUrl;
        }

        var url = _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == _section)?.Url;

        // Three sections out of twenty-five have no page of their own: the
        // site's table does not name them. We then stay on the one that lists
        // them all, rather than sending back to a quest that is not in
        // question on screen.
        return string.IsNullOrWhiteSpace(url) ? IndexUrl : url;
    }
}

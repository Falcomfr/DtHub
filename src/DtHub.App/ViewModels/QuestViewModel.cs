using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;

using Serilog;

namespace DtHub.App.ViewModels;

/// <summary>
/// What the quest window shows: the search, its results, and the open quest.
/// </summary>
public sealed partial class QuestViewModel : ObservableObject, IDisposable
{
    private readonly QuestCatalogService _catalog;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly TouchNameService _touchNames;

    private readonly ItemSourceService _itemSources;

    public QuestViewModel(
        QuestCatalogService catalog, IDialogService dialogs, QuestTrackerViewModel tracker, SettingsService settings,
        TouchNameService touchNames, ItemSourceService itemSources)
    {
        _touchNames = touchNames;
        _itemSources = itemSources;
        Tracker = tracker;
        _settings = settings;
        tracker.ProgressChanged += (_, _) => Rescore();
        tracker.TeamChanged += (_, _) =>
        {
            _team = null;
            RedrawAfterRead();
        };
        tracker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuestTrackerViewModel.IsLinked))
            {
                OnPropertyChanged(nameof(ShowsFilters));
                OnPropertyChanged(nameof(ShowsSelection));
                OnPropertyChanged(nameof(ShowsSelectionPanel));

                // Unlinked: the batch stops at its next pause, the cookie it holds is no longer the player's wish.
                if (!tracker.IsLinked)
                {
                    CancelBatch();
                }
            }

            // Ticks are for one character: a batch must not write them for another. A read puts null in passing.
            if (e.PropertyName == nameof(QuestTrackerViewModel.SelectedCharacter)
                && tracker.SelectedCharacter is { } character && character.Id != _pickedFor)
            {
                Selecting = false;
            }

            // "Recommandées" says a read is under way rather than "Reliez le suivi" or "ne répond pas".
            if (e.PropertyName == nameof(QuestTrackerViewModel.IsReading) && !tracker.IsLinked && ShowsRecommended)
            {
                Rescore();
            }

            if (e.PropertyName is nameof(QuestTrackerViewModel.RewardLevel) or nameof(QuestTrackerViewModel.RewardXpBonus))
            {
                _ = RefreshRewardsAsync();
                Rescore();
            }
        };
        _catalog = catalog;
        _tree = new QuestTree(catalog, _sectionCounts);
        _dialogs = dialogs;
        Nodes.CollectionChanged += (_, _) =>
        {
            ListHasQuestLevels = QuestListColumns.HasQuestLevels(Nodes);
            ListHasQuestGains = QuestListColumns.HasQuestGains(Nodes);
        };
    }

    /// <summary>True when a quest row of the displayed list shows a gain: the column is kept only then.</summary>
    [ObservableProperty]
    private bool _listHasQuestGains;

    /// <summary>Every paired quest's rewards and objectives, by Papycha id; <c>null</c> until read.</summary>
    private IReadOnlyDictionary<int, TouchQuestFacts>? _facts;

    /// <summary>The bulk read in flight, so a second show does not start another.</summary>
    private Task _factsRead = Task.CompletedTask;

    /// <summary>The last ranking, dropped whenever what it depends on changes.</summary>
    private IReadOnlyDictionary<int, QuestScore>? _scores;

    private IReadOnlyDictionary<int, QuestScore> Scores => _scores ??=
        _facts is null || Tracker.RewardLevel <= 0
            ? new Dictionary<int, QuestScore>()
            : QuestProfit.Score(_catalog.Catalog.Quests, _facts, Tracker.Progress,
                Tracker.RewardLevel, Tracker.RewardXpBonus);

    /// <summary>What "Recommandées" can list: the ranking's quests, a series by the next quest of each chain and way.</summary>
    private IReadOnlyList<ProfitEntry>? _recommendable;

    private IReadOnlyList<ProfitEntry> Recommendable => _recommendable ??=
        _facts is null
            ? []
            : QuestProfit.Recommendable(
                QuestProfit.Entries(_catalog.Catalog.Quests, Scores, Tracker.Progress, _facts, Tracker.RewardLevel, Tracker.RewardXpBonus),
                Tracker.Progress, _facts);

    /// <summary>The game's dungeon names by game dungeon id, read with the facts.</summary>
    private IReadOnlyDictionary<int, string> _dungeonNames = new Dictionary<int, string>();

    /// <summary>The game dungeons' room counts by game dungeon id, read with the names.</summary>
    private IReadOnlyDictionary<int, int> _dungeonRooms = new Dictionary<int, int>();

    /// <summary>The item names the names cache holds, by game item id: the gift's hover names those.</summary>
    private IReadOnlyDictionary<int, string> _itemNames = new Dictionary<int, string>();

    private IReadOnlyDictionary<int, DungeonValue>? _dungeonValues;

    /// <summary>By Papycha dungeon, the recommendable quests it completes; none with a prerequisite, as in the lists.</summary>
    private IReadOnlyDictionary<int, DungeonValue> DungeonValues => _dungeonValues ??=
        _facts is null
            ? new Dictionary<int, DungeonValue>()
            : QuestProfit.DungeonValues([.. Recommendable.Where(e => !HasPrerequisite(e, Mine))], Scores, Tracker.Progress, _facts, Tracker.RewardLevel,
                Tracker.RewardXpBonus, _touchNames.Table.Dungeons, _dungeonNames, _dungeonRooms);

    private IReadOnlyList<TeamEntry>? _team;

    /// <summary>The account's shared entries: one ranking per character, so kept with the ranking.</summary>
    private IReadOnlyList<TeamEntry> Team => _team ??=
        _facts is null ? [] : QuestPlanning.Team(_catalog.Catalog.Quests, _facts, Tracker.TeamProgress(), Tracker.RewardXpBonus);

    /// <summary>Drops the ranking and what is built on it, so the next read computes them again.</summary>
    private void ForgetRanking()
    {
        _scores = null;
        _recommendable = null;
        _dungeonValues = null;
        _team = null;
    }

    private bool _rescoreQueued;

    /// <summary>
    /// Ranks again, once, and redraws the shown list after the current dispatcher work: a tracker read changes the
    /// character, the level and the progress one after the other, each asking for a whole redraw.
    /// </summary>
    private void Rescore()
    {
        ForgetRanking();

        if (System.Windows.Application.Current?.Dispatcher is not { } dispatcher)
        {
            RedrawAfterRead();

            return;
        }

        if (_rescoreQueued)
        {
            return;
        }

        _rescoreQueued = true;
        _ = dispatcher.BeginInvoke(() =>
        {
            _rescoreQueued = false;
            RedrawAfterRead();
        });
    }

    /// <summary>The game read failure last logged: it stays set until a read succeeds.</summary>
    private Exception? _loggedTouchFailure;

    /// <summary>The game read's failure if not logged yet, so it is logged once, by the read that hit it first.</summary>
    private Exception? UnloggedTouchFailure()
    {
        if (_touchNames.LastFailure is not { } failure || failure == _loggedTouchFailure)
        {
            return null;
        }

        _loggedTouchFailure = failure;

        return failure;
    }

    /// <summary>
    /// Starts the bulk read in the background; the first one takes about two
    /// minutes, the next ones come from the week's cache.
    /// </summary>
    private void ReadFacts()
    {
        if (_factsRead.IsCompleted)
        {
            _factsRead = ReadFactsAsync();
        }
    }

    private async Task ReadFactsAsync()
    {
        // After the names: the facts read starts from them, and two first reads would fetch them twice.
        await NamesRead.ConfigureAwait(true);

        // Never throws but on cancellation, and none is asked for.
        _facts = await _touchNames.FactsAsync(TouchNameService.CurrentLanguage).ConfigureAwait(true);
        var names = await _touchNames.CachedAsync(TouchNameService.CurrentLanguage).ConfigureAwait(true);
        _dungeonNames = names?.Dungeons.ToDictionary(d => d.Key, d => d.Value.Name) ?? [];
        _dungeonRooms = names?.Dungeons.ToDictionary(d => d.Key, d => d.Value.Rooms) ?? [];
        _itemNames = names?.Items ?? [];

        if (UnloggedTouchFailure() is { } failure)
        {
            Log.Warning(failure, "The game's quest rewards and objectives could not be read");
        }

        Rescore();

        // The guide opened before the facts landed had no objectives to look up.
        _ = RefreshSourcesAsync();
    }

    // Each show reads again: past the week, the cache hands over to a fresh read.
    partial void OnIsWindowVisibleChanged(bool value)
    {
        if (value)
        {
            ReadFacts();
        }
        else
        {
            // Nobody reads the guide's item sources any more; the next show reads them again after the facts.
            _guideSources?.Cancel();
        }
    }

    /// <summary>True when a quest row of the displayed list has a level: the level column is kept only then.</summary>
    [ObservableProperty]
    private bool _listHasQuestLevels;

    /// <summary>The player's Papycha tracker, laid over the open quest.</summary>
    public QuestTrackerViewModel Tracker { get; }

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
    private void AddBySuccess(IEnumerable<QuestSummary> quests)
    {
        // Ordered on the whole section, filtered afterwards: a filter removes
        // rows, it never moves the ones left.
        foreach (var node in QuestTree.Keep(_tree.BySuccess([.. quests]), Passes, Tracker.Progress is not null && FilterDoable))
        {
            Nodes.Add(Marked(node));
        }
    }

    /// <summary>
    /// Lays the tracker's status, the prerequisites' state, the gain at the
    /// chosen level and the priority mark over a quest row; other rows pass
    /// through.
    /// </summary>
    private QuestNode Marked(QuestNode node)
    {
        if (node.Kind == QuestNodeKind.Success)
        {
            return node with { MarksAll = IsSelecting };
        }

        if (node.Dungeon is { } dungeon)
        {
            // Its run's time shows on every dungeon the game's table knows; the gains only with quests it advances.
            if (_touchNames.Table.Dungeons.TryGetValue(dungeon.Id, out var ids))
            {
                node = node with { Duration = QuestTime.Text(QuestTime.DungeonMinutes(ids.Dungeons, _dungeonRooms)) };
            }

            return DungeonValues.TryGetValue(dungeon.Id, out var value) ? Valued(node, value) : node;
        }

        if (node.Quest is not { } quest)
        {
            return node;
        }

        node = QuestTracking.Locked(node, Tracker.Progress, _tree.Chain) with
        {
            Status = QuestTracking.StatusOf(Tracker.Progress, quest.Id),
            Checked = IsSelecting ? _picked.Contains(quest.Id) : null,
        };
        var level = Tracker.RewardLevel;

        if (_facts is null || !_facts.TryGetValue(quest.Id, out var facts))
        {
            return node;
        }

        // The time does not depend on the level: it shows even when the gain cannot. In progress over several steps or
        // objectives, what is already done is unknown: time and gains are maximums, and no mark.
        var effort = QuestProfit.EffortOf(facts, quest);
        var uncertain = QuestProfit.IsUncertain(quest.Id, Tracker.Progress, facts);
        string AtMost(string text) => !uncertain || text.Length == 0 ? text : text[0] == '≈' ? "≤" + text[1..] : "≤ " + text;
        node = node with { Duration = AtMost(QuestTime.Text(effort.Minutes)) };

        // Unknown data hides the gain and the mark, as it hides the reward line.
        if (level <= 0 || QuestProfit.RewardOf(facts, level, Tracker.RewardXpBonus) is not { } reward)
        {
            return node;
        }

        var gain = new QuestGain(reward.Experience, reward.Kamas);

        var score = Scores.GetValueOrDefault(quest.Id);
        var tier = uncertain ? 0 : score?.Tier ?? 0;

        var (xp, kamas) = QuestGainText.Columns(gain);

        return node with
        {
            GainXp = AtMost(xp),
            GainKamas = kamas is null ? null : AtMost(kamas),
            Tier = tier,
            ProfitTip = (uncertain ? Strings.Get("QuestUncertain") + "\n" : string.Empty)
                + QuestGainText.Tip(gain, tier, score?.ByKamas ?? false, effort, facts.Steps.Length),
            OtherRewards = QuestRewardText.Others(reward, _itemNames),
        };
    }

    /// <summary>A hover held this long reads; a sweep across the list, whose tooltips open one after the other, does not.</summary>
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Where the items a quest asks for come from, for the gain's hover; null when it asks none or nothing is known.
    /// Read on demand after <see cref="HoverDelay"/>, cached by the service. Each hover passes its own token, which its
    /// closing cancels: a hover going never stops another's read.
    /// </summary>
    public async Task<string?> WhereToFindAsync(QuestSummary quest, CancellationToken ct)
    {
        try
        {
            await Task.Delay(HoverDelay, ct).ConfigureAwait(true);
            return JoinSources(await FindAsync(quest, ct).ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
            // The hover went: nobody reads this one any more.
            return null;
        }
    }

    public void Dispose()
    {
        CancelBatch();
        _guideSources?.Dispose();
    }

    private async Task<List<ItemSource>> FindAsync(QuestSummary quest, CancellationToken ct)
    {
        var sources = new List<ItemSource>();
        if (_facts is null || !_facts.TryGetValue(quest.Id, out var facts))
        {
            return sources;
        }

        foreach (var item in ItemSourceService.ItemsAsked(facts))
        {
            if (await _itemSources.SourceAsync(item, TouchNameService.CurrentLanguage, ct).ConfigureAwait(true) is { } source)
            {
                sources.Add(source);
            }
        }

        return sources;
    }

    /// <summary>Each item's sources, one block per item; null when none is known.</summary>
    private static string? JoinSources(List<ItemSource> sources) =>
        sources.Count == 0 ? null : string.Join("\n\n", sources.Select(ItemSourceText.Of));

    /// <summary>A dungeon row with what it is worth: the quests it finishes, their whole gains and its mark.</summary>
    private static QuestNode Valued(QuestNode node, DungeonValue value)
    {
        var (xp, kamas) = QuestGainText.Columns(value.Gain);
        var quests = string.Join(", ", value.Advances.Select(a => a.Title));

        return node with
        {
            GainXp = xp,
            GainKamas = kamas,
            Tier = value.Tier,
            ProfitTip = QuestGainText.Tip(value.Gain, effort: new QuestEffort(value.Minutes, [])) + "\n"
                + Strings.Format(value.Advances.Count == 1 ? "DungeonFinishesOne" : "DungeonFinishes", value.Advances.Count, quests),
        };
    }

    /// <summary>Past this many quests, a batch asks first: it takes minutes and writes on the player's account.</summary>
    private const int ConfirmAbove = 20;

    /// <summary>The quests ticked in selection mode, kept across lists until the mode is left.</summary>
    private readonly HashSet<int> _picked = [];

    /// <summary>The quest last ticked or unticked: Shift+click ticks from it.</summary>
    private int? _pickAnchor;

    /// <summary>The character selection mode was entered for.</summary>
    private string? _pickedFor;

    private CancellationTokenSource? _batch;

    /// <summary>"Sélection": quest rows carry a box, headings "Tout marquer terminé".</summary>
    [ObservableProperty]
    private bool _selecting;

    /// <summary>Selection mode while it can act: linked, list open.</summary>
    private bool IsSelecting => Selecting && Tracker.IsLinked;

    partial void OnSelectingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsSelectionPanel));
        _pickedFor = Tracker.SelectedCharacter?.Id;

        if (!value)
        {
            _picked.Clear();
            _pickAnchor = null;
            PickedCount = 0;
            BatchSummary = string.Empty;
        }

        Redraw();
    }

    /// <summary>The selection toggle: only once linked, on the list.</summary>
    public bool ShowsSelection => Tracker.IsLinked && IsListOpen;

    /// <summary>The batch line under the toolbar: in selection mode where it can act, and while a batch runs (its Cancel).</summary>
    public bool ShowsSelectionPanel => (Selecting && ShowsSelection) || BatchRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkPickedLabel))]
    [NotifyPropertyChangedFor(nameof(CanMarkPicked))]
    private int _pickedCount;

    public string MarkPickedLabel => Strings.Format("BatchMarkPicked", PickedCount);

    public bool CanMarkPicked => PickedCount > 0 && !BatchRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMarkPicked))]
    [NotifyPropertyChangedFor(nameof(ShowsSelectionPanel))]
    private bool _batchRunning;

    /// <summary>"42 / 140", the pass after the first; the count goes down when a read finds lost writes.</summary>
    [ObservableProperty]
    private string _batchProgress = string.Empty;

    [ObservableProperty]
    private string _batchSummary = string.Empty;

    /// <summary>
    /// Ticks or unticks a quest row; with <paramref name="range"/>, every quest row from the last one ticked takes
    /// the same state.
    /// </summary>
    public void Pick(QuestNode node, bool range)
    {
        ArgumentNullException.ThrowIfNull(node);

        var index = IndexOfNode(node);

        if (BatchRunning || index < 0 || node is not { Checked: { } was, Quest: { } quest })
        {
            return;
        }

        var on = !was;
        var (from, to) = (index, index);

        if (range && _pickAnchor is { } anchorId
            && Enumerable.Range(0, Nodes.Count).FirstOrDefault(i => Nodes[i].Quest?.Id == anchorId, -1) is >= 0 and var anchor)
        {
            (from, to) = (Math.Min(anchor, index), Math.Max(anchor, index));
        }

        for (var i = from; i <= to; i++)
        {
            // A range ticks only what "Tout marquer terminé" would send: an ignored, blocked or repeatable row is ticked
            // by its own click, never swept in by a Shift+click.
            if (Nodes[i] is not { Checked: { } state, Quest: { } q } row
                || (on && i != index && QuestBatch.ToMarkDone([q], Tracker.Progress).Count == 0))
            {
                continue;
            }

            if (on)
            {
                _picked.Add(q.Id);
            }
            else
            {
                _picked.Remove(q.Id);
            }

            if (state != on)
            {
                Nodes[i] = row with { Checked = on };
            }
        }

        _pickAnchor = quest.Id;
        PickedCount = _picked.Count;

        // A replaced row drops the highlight of the open guide.
        SelectCurrent();
    }

    private int IndexOfNode(QuestNode node)
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            if (ReferenceEquals(Nodes[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The ticked quests, but those already done: ticked by hand, an ignored or repeatable one is meant.</summary>
    public Task MarkPickedAsync() =>
        RunBatchAsync([.. _catalog.Catalog.Quests
            .Where(q => _picked.Contains(q.Id) && QuestTracking.StatusOf(Tracker.Progress, q.Id) != QuestStatus.Completed)
            .Select(q => q.Id)
            .Distinct()]);

    /// <summary>
    /// "Tout marquer terminé" on a heading: an achievement's quests listed under it, or a zone's quests its row counts
    /// (the filters applied), through <see cref="QuestBatch.ToMarkDone"/> (done, ignored, blocked and repeatable quests
    /// stay out). Either way the player marks what the list shows.
    /// </summary>
    public Task MarkAllAsync(QuestNode heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        var index = IndexOfNode(heading);

        // A heading redrawn away since the click: nothing to read under it.
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        IEnumerable<QuestSummary> quests = heading.Kind == QuestNodeKind.Success
            ? Nodes.Skip(index + 1).TakeWhile(n => n.InSuccess).Select(n => n.Quest).OfType<QuestSummary>().ToList()
            : _catalog.InSection(heading.Id).Where(Passes);

        return RunBatchAsync(QuestBatch.ToMarkDone(quests, Tracker.Progress));
    }

    private async Task RunBatchAsync(IReadOnlyList<int> ids)
    {
        if (BatchRunning)
        {
            return;
        }

        if (ids.Count == 0)
        {
            BatchSummary = Strings.Get("BatchNothing");

            return;
        }

        if (ids.Count > ConfirmAbove
            && !_dialogs.Confirm(Tracker.SelectedCharacter is { Name.Length: > 0 } character
                ? Strings.Format("BatchConfirm", ids.Count, character.Name)
                : Strings.Format("BatchConfirmNoName", ids.Count)))
        {
            return;
        }

        using var cancel = new CancellationTokenSource();
        _batch = cancel;
        BatchRunning = true;
        BatchSummary = string.Empty;
        BatchProgress = QuestBatchText.Progress(new QuestBatchProgress(0, ids.Count, 1));

        try
        {
            var progress = new Progress<QuestBatchProgress>(p =>
            {
                // A report posted after the end must not bring the line back.
                if (BatchRunning)
                {
                    BatchProgress = QuestBatchText.Progress(p);
                }
            });
            var result = await Tracker.MarkDoneAsync(ids, progress, cancel.Token).ConfigureAwait(true);

            if (result is null)
            {
                BatchSummary = Strings.Get("BatchNotStarted");

                return;
            }

            // What is left ticked is what the player may want to try again.
            _picked.ExceptWith(result.Done);
            PickedCount = _picked.Count;
            BatchSummary = QuestBatchText.Summary(
                result, id => _catalog.Catalog.Quests.FirstOrDefault(q => q.Id == id)?.Title ?? QuestTree.Text(id));
        }
        finally
        {
            _batch = null;
            BatchRunning = false;
        }
    }

    /// <summary>Stops the batch after the write under way; the summary says what the site acknowledged.</summary>
    public void CancelBatch() => _batch?.Cancel();

    private bool Passes(QuestSummary quest) =>
        QuestTracking.Passes(Tracker.Progress, quest.Id, FilterDoable, Startable(quest));

    private bool Startable(QuestSummary quest) =>
        FilterDoable && Tracker.Progress is { } progress && _tree.Chain is { } chain
        && QuestTracking.Doable(quest, progress, _facts?.GetValueOrDefault(quest.Id), Tracker.RewardLevel, chain);

    /// <summary>The list's "Faisables" filter, remembered.</summary>
    [ObservableProperty]
    private bool _filterDoable;

    partial void OnFilterDoableChanged(bool value) => FiltersChanged();

    /// <summary>True while the remembered filters are put back: nothing to redraw or save.</summary>
    private bool _loadingFilters;

    private void FiltersChanged()
    {
        if (_loadingFilters)
        {
            return;
        }

        Redraw();
        _ = _settings.SetQuestFilterAsync(FilterDoable);
    }

    /// <summary>
    /// Redraws after a tracker read. The read runs in a refresh nobody
    /// awaits: a failure here would vanish with that task, so it is logged.
    /// </summary>
    private void RedrawAfterRead()
    {
        try
        {
            Redraw();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Suivi Papycha : la liste des guides n'a pas pu être redessinée.");
        }
    }

    /// <summary>
    /// Draws the open level again after the marks or the filters changed.
    /// Before the first display it does nothing.
    /// </summary>
    private void Redraw()
    {
        // The root always has rows, so an empty root means never shown; an
        // empty deeper level is a filter's doing and must come back.
        if (_section == 0 && Nodes.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Query))
        {
            // "Suivantes" comes from the tracker: once it is gone (unlinked,
            // lost, unreadable) the level has nothing left to hold.
            ShowSection(_section == QuestTree.NextSection && !NextQuests().Any()
                ? QuestTree.RootSection
                : _section);
        }
        else
        {
            ShowSearch(Query);
        }

        // Keeps the highlight on the open guide through the redraw.
        SelectCurrent();
    }

    /// <summary>Open section, or zero at the root.</summary>
    private int _section;

    /// <summary>The displayed quest, when there is one.</summary>
    private QuestSummary? _current;

    /// <summary>The displayed quest, when there is one.</summary>
    public QuestSummary? CurrentQuest => _current;

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
    [NotifyPropertyChangedFor(nameof(ShowsFilters))]
    [NotifyPropertyChangedFor(nameof(ShowsRecommended))]
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
    [NotifyPropertyChangedFor(nameof(ShowsFilters))]
    [NotifyPropertyChangedFor(nameof(ShowsRecommended))]
    [NotifyPropertyChangedFor(nameof(ShowsSelection))]
    [NotifyPropertyChangedFor(nameof(ShowsSelectionPanel))]
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
    /// The "to do" and "available" filters: only where they act, the lists of
    /// quests, linked and not searching. A search is never filtered, a
    /// guide has nothing to filter, and "Recommandées" lists only what is
    /// doable already.
    /// </summary>
    public bool ShowsFilters => Tracker.IsLinked && IsListOpen && Query.Length == 0 && _section != QuestTree.ProfitSection;

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
    public bool ShowsPage => !IsListOpen && !IsLoadingPage && IsWindowVisible && !ShowsOfficialSteps;

    /// <summary>
    /// The list shows the game's names in any language but French, where
    /// Papycha's own are kept: the credit goes with them, once they are read.
    /// </summary>
    public bool ShowsOfficialNamesCredit => TouchNameService.CurrentLanguage != "fr" && _catalog.UsesGameNames;

    /// <summary>The guide is Papycha's, in French: the game's own wording only exists to compare, in another language.</summary>
    public bool OffersOfficialSteps =>
        _current is { } quest
        && TouchNameService.CurrentLanguage != "fr"
        && _touchNames.Table.Quests.ContainsKey(quest.Id);

    /// <summary>
    /// True when the game's steps take the web view's place. The view is
    /// removed, not covered, for the reason given at <see cref="ShowsPage"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPage))]
    [NotifyPropertyChangedFor(nameof(ShowsOfficialStepsUnavailable))]
    private bool _showsOfficialSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOfficialStepsUnavailable))]
    private bool _isLoadingOfficialSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOfficialStepsUnavailable))]
    private IReadOnlyList<TouchStep> _officialSteps = [];

    /// <summary>The read came back empty: unpaired, or the game's server refused.</summary>
    public bool ShowsOfficialStepsUnavailable =>
        ShowsOfficialSteps && !IsLoadingOfficialSteps && OfficialSteps.Count == 0;

    /// <summary>Papycha quest the steps in <see cref="OfficialSteps"/> belong to.</summary>
    private int _officialStepsOf;

    /// <summary>The reward line under the title; empty hides it.</summary>
    [ObservableProperty]
    private string _rewardText = string.Empty;

    /// <summary>
    /// Reads the shown quest's steps in the background (once: the official steps view reuses them)
    /// and words what they give at the tracker's level.
    /// </summary>
    private async Task RefreshRewardsAsync()
    {
        if (_current is not { } quest || Tracker.RewardLevel <= 0)
        {
            RewardText = string.Empty;

            return;
        }

        var steps = OfficialSteps;

        if (_officialStepsOf != quest.Id)
        {
            steps = await _touchNames.StepsAsync(quest.Id, TouchNameService.CurrentLanguage).ConfigureAwait(true);

            if (_current?.Id != quest.Id)
            {
                return;
            }

            if (steps.Count == 0)
            {
                return;
            }

            OfficialSteps = steps;
            _officialStepsOf = quest.Id;
        }

        var names = (await _touchNames.CachedAsync(TouchNameService.CurrentLanguage).ConfigureAwait(true))?.Items;

        if (_current?.Id != quest.Id)
        {
            return;
        }

        var level = Tracker.RewardLevel;
        // The facts hold one game quest, as the lists do; the guide's steps may show two paired ones ("L'Ascension").
        var total = names is null || level <= 0 ? null
            : _facts?.GetValueOrDefault(quest.Id) is { } facts ? QuestProfit.RewardOf(facts, level, Tracker.RewardXpBonus)
            : QuestRewards.For(steps, level, Tracker.RewardXpBonus);

        RewardText = total is null ? string.Empty : QuestRewardText.Format(total, level, Tracker.RewardXpBonus, names!);
    }

    /// <summary>
    /// Says under the reward line where the shown quest's items come from. Reads the game only for items not seen
    /// this month, in the background; a refusing server is spared by the service's cooldown.
    /// </summary>
    private async Task RefreshSourcesAsync()
    {
        _guideSources?.Cancel();

        // Hidden: the next show reads the facts again, then this.
        if (_current is not { } quest || !IsWindowVisible)
        {
            return;
        }

        // Not disposed when replaced: the service may still link to its token.
        _guideSources = new CancellationTokenSource();

        try
        {
            var sources = await FindAsync(quest, _guideSources.Token).ConfigureAwait(true);

            if (_current?.Id == quest.Id)
            {
                SourcesText = JoinSources(sources) ?? string.Empty;
                SourcesLine = ItemSourceText.Line(sources);
            }
        }
        catch (OperationCanceledException)
        {
            // Another quest is shown, or the facts landed and a fresh read took over: this one is read by nobody.
        }
    }

    /// <summary>The guide's item-source read, cancelled as soon as another quest is shown.</summary>
    private CancellationTokenSource? _guideSources;

    private void SetShownQuest(QuestSummary? quest)
    {
        if (_current?.Id != quest?.Id)
        {
            _guideSources?.Cancel();
        }

        _current = quest;
    }

    /// <summary>Where the open quest's items come from, on hover of the line under the reward; empty hides it.</summary>
    [ObservableProperty]
    private string _sourcesText = string.Empty;

    /// <summary>The same on one line, three sources at most (<see cref="ItemSourceText.Line"/>).</summary>
    [ObservableProperty]
    private string _sourcesLine = string.Empty;

    private void ResetOfficialSteps()
    {
        RewardText = string.Empty;
        SourcesText = string.Empty;
        SourcesLine = string.Empty;
        ShowsOfficialSteps = false;
        OfficialSteps = [];
        _officialStepsOf = 0;
        OnPropertyChanged(nameof(OffersOfficialSteps));
    }

    /// <summary>Its own command: the load below disables its command for as long as it runs, and leaving must stay possible.</summary>
    [RelayCommand]
    private void BackToGuide() => ShowsOfficialSteps = false;

    /// <summary>Swaps the guide for the game's steps, and back.</summary>
    [RelayCommand]
    public async Task ToggleOfficialStepsAsync()
    {
        if (ShowsOfficialSteps)
        {
            ShowsOfficialSteps = false;

            return;
        }

        if (_current is not { } quest)
        {
            return;
        }

        ShowsOfficialSteps = true;

        if (_officialStepsOf == quest.Id)
        {
            return;
        }

        IsLoadingOfficialSteps = true;

        try
        {
            var steps = await _touchNames.StepsAsync(quest.Id, TouchNameService.CurrentLanguage).ConfigureAwait(true);

            if (_current?.Id != quest.Id)
            {
                return;
            }

            if (steps.Count == 0 && UnloggedTouchFailure() is { } failure)
            {
                Log.Warning(failure, "The game's steps could not be read");
            }

            OfficialSteps = steps;
            _officialStepsOf = steps.Count == 0 ? 0 : quest.Id;
        }
        finally
        {
            IsLoadingOfficialSteps = false;
        }
    }

    /// <summary>
    /// True when the view's place is taken over by the loading indicator.
    /// </summary>
    public bool ShowsLoader => IsLoadingPage && !IsListOpen;

    /// <summary>
    /// True when the step arrows should be visible.
    ///
    /// They hold their place during loading, greyed out, when the steps are
    /// not yet known: the title would otherwise shift at every quest change.
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
        // Disk only, no network: the first opening has nothing in memory yet.
        await _catalog.LoadCachedAsync(cancellationToken).ConfigureAwait(true);

        var before = QuestTally.Of(_catalog.Catalog);

        var saved = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);
        _loadingFilters = true;
        FilterDoable = saved.QuestFilterDoable;
        ProfitKamas = saved.ProfitKamas;
        ProfitDungeons = saved.ProfitDungeons;
        ProfitSeries = saved.ProfitSeries;
        _loadingFilters = false;

        IsBusy = true;

        // **Yesterday's tree is better than an empty tree.** The window used
        // to show "Quests 0" until the network check returned, though the
        // catalogue was on disk. LoadCachedAsync loaded it above, so we show
        // it right away, with the status line saying elsewhere that an
        // update is in progress.
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
        OnPropertyChanged(nameof(ShowsOfficialNamesCredit));

        // Not awaited: names that arrive later show at the next opening.
        NamesRead = ReadNamesAsync();
        ReadFacts();
    }

    /// <summary>
    /// The chain index was built from the catalog before the names came:
    /// rebuilt from the localized one so prerequisites resolve again.
    /// </summary>
    private async Task ReadNamesAsync()
    {
        await _catalog.RefreshNamesAsync(CancellationToken.None).ConfigureAwait(true);
        _tree.Chain = new QuestChainIndex(_catalog.Catalog.Quests);
        ForgetRanking();

        // The rows drawn before hold the French records: clicking one would open it outside the localized chain.
        RedrawAfterRead();
        OnPropertyChanged(nameof(ShowsOfficialNamesCredit));
    }

    /// <summary>The names refresh started by the last load; never faults.</summary>
    public Task NamesRead { get; private set; } = Task.CompletedTask;

    /// <summary>Why the game's names could not be read, logged by the window.</summary>
    public Exception? NamesFailure => _catalog.NamesFailure;

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
        ForgetRanking();

        IsBusy = false;

        // What the reread brought back, when there was something before to
        // compare it with: the cached catalogue, or the one shipped with the
        // application on a first run. Only a catalogue that was empty stays
        // silent: announcing "seven hundred and eighty-two quests more" would
        // teach nothing.
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
        OnPropertyChanged(nameof(ShowsRecommended));
        OnPropertyChanged(nameof(ShowsFilters));
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
        OnPropertyChanged(nameof(ShowsRecommended));
        OnPropertyChanged(nameof(ShowsFilters));

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

            // Only the paths no dungeon takes: the others sit under theirs.
            if (_tree.Paths(PathSide.Dungeons).Count > 0)
            {
                Nodes.Add(_tree.PathBranch(PathSide.Dungeons, QuestTree.DungeonPathSection));
            }

            foreach (var node in _tree.DungeonNodes())
            {
                Nodes.Add(Marked(node));
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
            foreach (var node in _tree.WithPaths(_tree.Fighting(kind).OrderBy(d => d.Level)
                         .ThenBy(d => d.Title, StringComparer.CurrentCulture)
                         .Select(QuestTree.NodeOf)))
            {
                Nodes.Add(Marked(node));
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
                Nodes.Add(QuestTree.NodeOf(path));
            }

            return;
        }

        if (section == QuestTree.RootSection)
        {
            Breadcrumb = Strings.Get("QuestAreaCrumb");
            SetBack(target: 0);

            // With a filter on, a zone with nothing left to show would open
            // onto an empty list.
            var filtering = Tracker.Progress is not null && FilterDoable;
            var next = NextQuests().ToList();

            if (next.Count > 0 && (!filtering || next.Any(Passes)))
            {
                Nodes.Add(new QuestNode(
                    QuestNodeKind.Branch,
                    Strings.Get("NextQuests"),
                    QuestTree.Nombre(next.Count),
                    Id: QuestTree.NextSection,
                    Glyph: QuestNodeGlyph.Family));
            }

            // Only the paths no zone takes: the others head their zone.
            if (_tree.Paths(PathSide.Quests).Count > 0)
            {
                Nodes.Add(_tree.PathBranch(PathSide.Quests, QuestTree.QuestPathSection));
            }

            // A subheading is held back until a branch after it survives.
            QuestNode? header = null;

            foreach (var branch in _tree.Branches())
            {
                if (branch.Kind != QuestNodeKind.Branch)
                {
                    if (branch.Kind == QuestNodeKind.Header && filtering)
                    {
                        header = branch;
                    }
                    else
                    {
                        Nodes.Add(branch);
                    }

                    continue;
                }

                var quests = _catalog.InSection(branch.Id);
                var shown = filtering ? quests.Count(Passes) : quests.Count;

                if (shown > 0)
                {
                    if (header is not null)
                    {
                        Nodes.Add(header);
                        header = null;
                    }

                    // The count follows the filters. Linked, it is said once (D219): unfiltered by "done/total"
                    // on the right, the label's own count dropped (it counted repeatables, the right does not); under
                    // the filter by the label, the right column hidden. A zone of repeatables only keeps its label's.
                    var tally = QuestTracking.ZoneProgress(
                        Tracker.Progress, [.. quests.DistinctBy(q => q.Id)], FilterDoable, Startable);
                    Nodes.Add(branch with
                    {
                        MarksAll = IsSelecting,
                        Label = filtering ? $"{_tree.NameOf(branch.Id)} ({QuestTree.Text(shown)})"
                            : tally is { Total: > 0 } ? _tree.NameOf(branch.Id)
                            : branch.Label,
                        Detail = filtering ? QuestLevelRange.Of(quests.Where(Passes)) : branch.Detail,
                        Tally = tally,
                    });
                }
            }

            return;
        }

        if (section == QuestTree.NextSection)
        {
            Breadcrumb = Strings.Get("QuestAreaCrumb") + Separator + Strings.Get("NextQuests");
            SetBack(target: QuestTree.RootSection);

            // Already in order: the site's when it suggests, play order otherwise.
            foreach (var quest in NextQuests().Where(Passes))
            {
                Nodes.Add(Marked(_tree.NodeOf(quest)));
            }

            return;
        }

        if (section == QuestTree.ProfitSection)
        {
            ShowProfit();

            return;
        }

        Breadcrumb = Strings.Get("QuestAreaCrumb") + Separator + _tree.NameOf(section);
        SetBack(target: QuestTree.RootSection);

        // The ways to get there come before what is done there.
        var paths = _tree.PathsIn(section);

        for (var i = 0; i < paths.Count; i++)
        {
            Nodes.Add(QuestTree.NodeOf(paths[i]) with { Spaced = i == paths.Count - 1 });
        }

        AddBySuccess(_catalog.InSection(section));
    }

    /// <summary>The "Recommandées" view is on screen: its button shows pressed.</summary>
    public bool ShowsRecommended => IsListOpen && _section == QuestTree.ProfitSection && Query.Length == 0;

    /// <summary>Where "Recommandées" was opened from: its section, and whether the list was open.</summary>
    private (int Section, bool ListOpen) _beforeRecommended = (QuestTree.RootSection, true);

    /// <summary>
    /// Opens the recommended quests from wherever the reader is; pressed
    /// again, puts back the section and the guide or list they left.
    /// </summary>
    public void ToggleRecommended()
    {
        if (ShowsRecommended)
        {
            var (section, listOpen) = _beforeRecommended;
            ShowSection(section);
            IsListOpen = listOpen || !HasQuest;

            return;
        }

        // Read before clearing the query, which redraws the section left.
        _beforeRecommended = (_section == QuestTree.ProfitSection ? _beforeRecommended.Section : _section, IsListOpen);
        Query = string.Empty;
        ShowSection(QuestTree.ProfitSection);
        IsListOpen = true;
    }

    /// <summary>"Recommandées" for the account: what two characters or more can do, gains summed over them.</summary>
    [ObservableProperty]
    private bool _profitTeam;

    partial void OnProfitTeamChanged(bool value) => Redraw();

    /// <summary>Sets the "Moi / Équipe" choice; told again so a toggle clicked while already on comes back on.</summary>
    public void ShowTeam(bool team)
    {
        ProfitTeam = team;
        OnPropertyChanged(nameof(ProfitTeam));
    }

    /// <summary>
    /// A row of the short lists: what it gains, in how many minutes, where ties put it, and what its toggles read: a
    /// dungeon run or not, a prerequisite DT Hub cannot check or not (<see cref="QuestProfit.HasPrerequisite"/>), never shown.
    /// </summary>
    private sealed record ProfitRow(
        QuestGain Gain, int Minutes, QuestSummary First, QuestNode Node, bool Dungeon, bool Prerequisite, ProfitEntry? Entry = null);

    /// <summary>"Recommandées" shows its "Kamas" tab rather than its "XP" one, remembered.</summary>
    [ObservableProperty]
    private bool _profitKamas;

    /// <summary>"Recommandées" keeps dungeons and the quests with a dungeon run ("Donjons inclus"), remembered.</summary>
    [ObservableProperty]
    private bool _profitDungeons = true;

    /// <summary>"Recommandées" lists series runs ("Succès") rather than single quests ("Quêtes"), remembered.</summary>
    [ObservableProperty]
    private bool _profitSeries;

    /// <summary>The line under "Quêtes recommandées" saying how the list is ranked, for one character or the team.</summary>
    [ObservableProperty]
    private string _profitHow = string.Empty;

    /// <summary>The detail of <see cref="ProfitHow"/>, on its info glyph.</summary>
    [ObservableProperty]
    private string _profitHowTip = string.Empty;

    /// <summary>The key of what "Recommandées" waits for or lacks in place of its rows; empty when they show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfitWaitText))]
    [NotifyPropertyChangedFor(nameof(ShowsProfitWait))]
    [NotifyPropertyChangedFor(nameof(ProfitWaitSpins))]
    [NotifyPropertyChangedFor(nameof(ProfitCanRetry))]
    private string _profitWait = string.Empty;

    public string ProfitWaitText => ProfitWait.Length > 0 ? Strings.Get(ProfitWait) : string.Empty;

    public bool ShowsProfitWait => ProfitWait.Length > 0;

    public bool ProfitWaitSpins => Core.Papycha.ProfitWait.Spins(ProfitWait);

    public bool ProfitCanRetry => Core.Papycha.ProfitWait.CanRetry(ProfitWait);

    partial void OnProfitKamasChanged(bool value) => ProfitFiltersChanged();

    partial void OnProfitDungeonsChanged(bool value) => ProfitFiltersChanged();

    partial void OnProfitSeriesChanged(bool value) => ProfitFiltersChanged();

    /// <summary>Picks the "Quêtes" or "Succès" mode; told again so a mode clicked while already on comes back on.</summary>
    public void ShowSeries(bool series)
    {
        ProfitSeries = series;
        OnPropertyChanged(nameof(ProfitSeries));
    }

    /// <summary>Picks the "XP" or "Kamas" tab; told again so a tab clicked while already on comes back on.</summary>
    public void ShowKamas(bool kamas)
    {
        ProfitKamas = kamas;
        OnPropertyChanged(nameof(ProfitKamas));
    }

    private void ProfitFiltersChanged()
    {
        if (_loadingFilters)
        {
            return;
        }

        Redraw();
        _ = _settings.SetProfitFiltersAsync(ProfitKamas, ProfitDungeons, ProfitSeries);
    }

    // "vire carrément 'Avec prérequis' et n'affiche pas de quêtes non faisables directement" (2026-10-24).
    private bool Kept(ProfitRow row) => (ProfitDungeons || !row.Dungeon) && !row.Prerequisite;

    /// <summary>
    /// A quest row of the short lists, its toggles read from the quest's objectives (a dungeon run, or a dungeon named
    /// as the dungeon rows find them) and from its needs for the row's characters (<paramref name="progress"/>).
    /// </summary>
    private ProfitRow QuestRow(QuestGain gain, ProfitEntry entry, QuestNode node, IReadOnlyCollection<TrackerProgress> progress)
    {
        var dungeon = _facts?.GetValueOrDefault(entry.Quests[0].Id) is { } facts
            && (QuestTime.HasDungeonRun(facts) || QuestProfit.DungeonsOf(facts, _dungeonNames).Count > 0);
        return new ProfitRow(gain, entry.Effort.Minutes, entry.Quests[0], node, dungeon, HasPrerequisite(entry, progress), entry);
    }

    /// <summary>The chosen character's progress, as the prerequisite check takes it.</summary>
    private IReadOnlyCollection<TrackerProgress> Mine => Tracker.Progress is { } progress ? [progress] : [];

    private bool HasPrerequisite(ProfitEntry entry, IReadOnlyCollection<TrackerProgress> progress) =>
        _tree.Chain is { } chain && QuestProfit.HasPrerequisite(entry.Quests[0], chain, progress);

    /// <summary>
    /// The game publishes no XP table (checked 2026-10-23), so the whole-level line never shows.
    /// shortcut: empty on purpose, fill it if the game or the user gives one.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, long> XpTable = new Dictionary<int, long>();

    /// <summary>
    /// The short list of the tab picked, XP or kamas: the doable quests, a series by its next one, and dungeons, best
    /// on that side per minute.
    /// </summary>
    private void ShowProfit()
    {
        Breadcrumb = Strings.Get("ProfitQuests");
        SetBack(target: _beforeRecommended.Section);

        // The toggle hides with a single character left: the team view must not stay on, empty.
        var team = ProfitTeam && Tracker.HasSeveralCharacters;
        ProfitHow = Strings.Get((team ? "ProfitHowTeam" : "ProfitHow") + (ProfitKamas ? "Kamas" : "Xp"));
        ProfitHowTip = Strings.Get(team ? "ProfitHowTeamTip" : "ProfitHowTip");

        // The ranking itself measured under 25 ms (2026-10-24), so it gets no line of its own.
        ProfitWait = Core.Papycha.ProfitWait.Of(
            Tracker.Link, Tracker.IsReading, _facts?.Count, _factsRead.IsCompleted, _touchNames.LastFailure is not null);

        if (ProfitWait.Length > 0)
        {
            return;
        }

        var tooFew = team && Tracker.TeamProgress().Count < 2;
        // A run has no time limit: a series takes the evening it takes, the row says how long.
        var rows = (ProfitSeries ? team ? TeamRunRows() : MyRunRows() : team ? TeamRows() : MyRows()).Where(Kept).ToList();
        var shown = QuestProfit.Shortlist(
            rows, r => r.Gain, r => r.Minutes, r => r.First, _catalog.Catalog.Quests, ProfitKamas,
            maxMinutes: ProfitSeries ? int.MaxValue : QuestProfit.ShortMinutes);

        // No priority mark here: the order and the tab say it, and the combined-score tiers contradicted the order.
        // No lock either: every row is startable for its characters, and a neutral lock made team rows taller.
        // The hover gives the rates the list ranks on.
        foreach (var row in shown)
        {
            var (tip, rate) = (row.Node.ProfitTip, QuestGainText.Rate(row.Gain, row.Minutes));
            Nodes.Add(row.Node with { Tier = 0, NeedsMet = true, ProfitTip = rate is null ? tip : tip is null ? rate : $"{tip}\n{rate}" });
        }

        if (shown.Count == 0)
        {
            Nodes.Add(new QuestNode(QuestNodeKind.Pending, Strings.Get(tooFew ? "ProfitPickTwo" : "NoResult")));
        }

        if (!ProfitKamas && !team
            && QuestPlanning.WholeLevel([.. shown.Select(r => r.Entry).OfType<ProfitEntry>()], Tracker.RewardLevel, XpTable) is { } line)
        {
            Nodes.Insert(0, new QuestNode(QuestNodeKind.Pending, Strings.Format("ProfitWholeLevel", line.Count, line.From, line.To)));
        }
    }

    /// <summary>The chosen character's entries and the dungeons that advance two of them or more.</summary>
    private List<ProfitRow> MyRows()
    {
        var rows = Recommendable.Select(e => QuestRow(e.Gain, e, EntryNode(e), Mine)).ToList();

        // A dungeon advancing one quest only would repeat that quest's row.
        foreach (var value in DungeonValues.Values.Where(v => v.Advances.Count >= 2))
        {
            if (_catalog.Catalog.Dungeons.FirstOrDefault(d => d.Id == value.PapychaDungeonId) is { } dungeon)
            {
                var node = QuestTree.NodeOf(dungeon) with { Glyph = QuestTree.GlyphOf(dungeon.Kind) };
                rows.Add(new ProfitRow(
                    value.Gain, value.Minutes, value.Advances[0].Quests[0], Valued(node, value) with { Duration = QuestTime.Text(value.Minutes) },
                    Dungeon: true, Prerequisite: false));
            }
        }

        return rows;
    }

    /// <summary>
    /// The account's shared entries, gains summed, the characters named on hover; a previous quest is a prerequisite
    /// unless every character of the entry has done it.
    /// </summary>
    private List<ProfitRow> TeamRows()
    {
        var team = Tracker.TeamProgress();
        return
        [
            .. Team.Select(t =>
            {
                var node = EntryNode(t.Entry);
                var progress = team.Where(m => t.Characters.Any(c => c.Id == m.Character.Id)).Select(m => m.Progress).ToList();
                var (xp, kamas) = QuestGainText.Columns(t.Gain);
                var count = Strings.Format("ProfitCharacters", t.Characters.Count);

                // The node was marked for the chosen character: his status, his "≤" and his level's gift are not the team's.
                return QuestRow(t.Gain, t.Entry, node with
                {
                    Status = QuestStatus.NotStarted,
                    Duration = QuestTime.Text(t.Entry.Effort.Minutes),
                    OtherRewards = null,
                    GainXp = xp,
                    GainKamas = kamas,
                    Tier = t.Tier,
                    Detail = node.Detail is { Length: > 0 } detail ? $"{detail} · {count}" : count,
                    ProfitTip = QuestGainText.Tip(t.Gain, effort: t.Entry.Effort, steps: StepsOf(t.Entry))
                        + "\n" + string.Join(", ", t.Characters.Select(c => c.Name)),
                }, progress);
            }),
        ];
    }

    /// <summary>The chosen character's series runs ("Succès"); the runs apply the two toggles themselves.</summary>
    private List<ProfitRow> MyRunRows() =>
        Tracker.Progress is not { } progress || _facts is null
            ? []
            : [.. QuestProfit.Runs(_catalog.Catalog.Quests, _facts, progress, Tracker.RewardLevel, Tracker.RewardXpBonus,
                    ProfitDungeons, _dungeonNames)
                .Select(r => RunRow(r, r.Gain, QuestGainText.Tip(r.Gain, effort: r.Effort) + "\n"
                    + string.Join("\n", r.Quests.Select(RunLine))))];

    /// <summary>
    /// The account's series runs, gains summed, the longest run's time and quests.
    /// Each quest's own gains differ by character, so the hover gives its time only.
    /// </summary>
    private List<ProfitRow> TeamRunRows() =>
        _facts is null
            ? []
            : [.. QuestPlanning.TeamRuns(_catalog.Catalog.Quests, _facts, Tracker.TeamProgress(), Tracker.RewardXpBonus,
                    ProfitDungeons, _dungeonNames)
                .Select(t =>
                {
                    var tip = QuestGainText.Tip(t.Gain, effort: t.Entry.Effort) + "\n"
                        + string.Join("\n", t.Entry.Quests.Select(q => $"{q.Title} · {QuestTime.Text(QuestProfit.EffortOf(_facts[q.Id], q).Minutes)}"))
                        + "\n" + string.Join(", ", t.Characters.Select(c => c.Name));
                    var row = RunRow(t.Entry, t.Gain, tip);
                    var node = row.Node;
                    var count = Strings.Format("ProfitCharacters", t.Characters.Count);
                    // As in TeamRows: the chosen character's status and level's gift are not the team's.
                    return row with
                    {
                        Node = node with
                        {
                            Status = QuestStatus.NotStarted,
                            OtherRewards = null,
                            Detail = node.Detail is { Length: > 0 } detail ? $"{detail} · {count}" : count,
                        },
                    };
                })];

    /// <summary>
    /// A series run as a row: the series' name, then "· voie En semant, se ment" while its way is not taken,
    /// "· Zone · 3 quêtes (2-4/11)", the run's time and gains, the gift when one of its quests gives more; a click opens
    /// its first quest.
    /// </summary>
    private ProfitRow RunRow(ProfitEntry run, QuestGain gain, string tip)
    {
        var first = run.Quests[0];
        var node = Marked(_tree.NodeOf(first));
        var zone = first.SectionId == 0 ? string.Empty : " · " + _tree.NameOf(first.SectionId);
        // Places along the run's way: the other way of "Agriculture ou alchimie" is not counted.
        var series = QuestPlayOrder.Way(_catalog.Catalog.Quests, run.Quests).Select(q => q.Id).ToList();
        var place = Strings.Format("ProfitRunQuests", run.Quests.Count, series.IndexOf(first.Id) + 1, series.IndexOf(run.Quests[^1].Id) + 1, series.Count);
        // The way is named by its first quest while the player has not taken it: then each way has its row.
        var way = run.Path is { } path && run.Quests.Contains(path) && QuestTracking.StatusOf(Tracker.Progress, path.Id) != QuestStatus.InProgress
            ? " · " + Strings.Format("ProfitRunWay", path.Title)
            : string.Empty;
        var others = run.Quests
            .Select(q => _facts?.GetValueOrDefault(q.Id) is { } f && QuestProfit.RewardOf(f, Tracker.RewardLevel, Tracker.RewardXpBonus) is { } r
                ? QuestRewardText.Others(r, _itemNames) : null)
            .OfType<string>().ToList();
        var (xp, kamas) = QuestGainText.Columns(gain);
        node = node with
        {
            Label = run.Title,
            Suffix = $"{way}{zone} · {place}",
            Duration = QuestTime.Text(run.Effort.Minutes),
            GainXp = xp,
            GainKamas = kamas,
            Tier = 0,
            ProfitTip = tip,
            OtherRewards = others.Count > 0 ? string.Join("\n", others) : null,
        };
        return new ProfitRow(gain, run.Effort.Minutes, first, node, Dungeon: false, Prerequisite: false, run);
    }

    /// <summary>A run's quest in its hover: its own XP, kamas and time.</summary>
    private string RunLine(QuestSummary quest)
    {
        var facts = _facts![quest.Id];
        var gain = QuestProfit.GainOf(facts, Tracker.RewardLevel, Tracker.RewardXpBonus)!;
        return Strings.Format("ProfitRunLine", quest.Title, QuestGainText.Compact(gain.Xp), QuestGainText.Compact(gain.Kamas),
            QuestTime.Text(QuestProfit.EffortOf(facts, quest).Minutes));
    }

    /// <summary>A recommended quest as a row: its zone, then its series and place in it ("· Agriculture et Alchimie 3/11").</summary>
    private QuestNode EntryNode(ProfitEntry entry)
    {
        var first = entry.Quests[0];
        var node = Marked(_tree.NodeOf(first));
        var zone = first.SectionId == 0 ? string.Empty : _tree.NameOf(first.SectionId);
        var series = QuestPlayOrder.Position(_catalog.Catalog.Quests, first) is { } at ? $" · {first.SuccessName} {at.Rank}/{at.Count}" : string.Empty;
        var suffix = (zone.Length > 0 ? " · " + zone : string.Empty) + series;
        node = node with { Suffix = suffix.Length > 0 ? suffix : null };

        // No tier in the hover either: the short lists carry no mark (see ShowProfit), and the tier's side would
        // contradict the list ("pour l'XP" in the kamas list).
        return node.ProfitTip is null ? node : node with { ProfitTip = QuestGainText.Tip(entry.Gain, effort: entry.Effort, steps: StepsOf(entry)) };
    }

    private int StepsOf(ProfitEntry entry) => _facts?.GetValueOrDefault(entry.Quests[0].Id)?.Steps.Length ?? 1;

    /// <summary>The catalogue quests to suggest next: the site's, or read from the previous quests done.</summary>
    private IReadOnlyList<QuestSummary> NextQuests() => QuestTracking.NextQuests(Tracker.Progress, _catalog.Catalog.Quests, _facts, Tracker.RewardLevel);

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
                    $"{QuestSectionTitle.Of(zone)} ({_sectionCounts.GetValueOrDefault(zone.Id)})",
                    QuestLevelRange.Of(_catalog.InSection(zone.Id)),
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
                    Nodes.Add(Marked(_tree.NodeOf(quest) with { InSuccess = true }));
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
                Nodes.Add(Marked(QuestTree.NodeOf(place) with { Glyph = QuestTree.GlyphOf(kind) }));
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
                Nodes.Add(Marked(_tree.NodeOf(quest)));
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

        // A row or a link may still carry the record from before the names arrived.
        quest = _catalog.Catalog.Quests.FirstOrDefault(q => q.Id == quest.Id) ?? quest;

        Tracker.ShowQuest(quest.Id);

        SetShownQuest(quest);

        ResetOfficialSteps();
        _currentDungeon = null;
        _currentPath = null;

        if (anchor)
        {
            Anchor(SectionSeen(quest), quest.Url);
        }
        CurrentUrl = quest.Url;
        QuestTitle = quest.Title;
        HasQuest = true;

        SetNeighbours(quest);

        // The next page is not loaded yet: keeping the previous one's steps
        // would let the arrows walk a guide that is no longer shown.
        ResetSteps([]);
        SetStep(-1);

        _ = RefreshRewardsAsync();
        _ = RefreshSourcesAsync();
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

        // See the quests overload.
        dungeon = _catalog.Catalog.Dungeons.FirstOrDefault(d => d.Id == dungeon.Id) ?? dungeon;

        Tracker.ShowQuest(null);

        SetShownQuest(null);

        ResetOfficialSteps();
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

        ChainText = dungeon.Key.Length > 0 ? dungeon.Key : string.Empty;
        ChainStep = string.Empty;
        PreviousQuest = null;
        NextQuest = null;

        ResetSteps([]);
        SetStep(-1);
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

        Tracker.ShowQuest(null);

        SetShownQuest(null);

        ResetOfficialSteps();
        _currentDungeon = null;
        _currentPath = path;
        _neighbours = default;

        if (anchor)
        {
            Anchor(_tree.SectionOf(path), path.Url);
        }
        CurrentUrl = path.Url;
        QuestTitle = path.Title;
        HasQuest = true;

        ChainText = Strings.Get("Path");
        ChainStep = string.Empty;
        PreviousQuest = null;
        NextQuest = null;

        ResetSteps([]);
        SetStep(-1);
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

    [ObservableProperty]
    private int _stepIndex = -1;

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
        IReadOnlyList<QuestStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

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
    /// Scrolling or an arrow has changed step.
    /// </summary>
    public void SetStep(int index)
    {
        StepIndex = index;

        var total = _steps.Count;

        CanGoPreviousStep = index > 0;
        CanGoNextStep = index >= 0 && index < total - 1;
    }

    /// <summary>
    /// Resets the steps and their count together.
    /// </summary>
    private void ResetSteps(IReadOnlyList<QuestStep> steps)
    {
        _steps = steps;
        HasSteps = steps.Count > 0;
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
        Tracker.ShowQuest(null);
        SetShownQuest(null);
        ResetOfficialSteps();
        _currentDungeon = null;
        _currentPath = null;
        _neighbours = default;

        CurrentUrl = link.Url;
        QuestTitle = link.Title;
        ChainText = string.Empty;
        ChainStep = string.Empty;
        HasQuest = true;
        IsListOpen = false;

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

        Tracker.ShowQuest(null);
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

    /// <summary>The same page, translated into the application's language: see PageTranslation.</summary>
    public void OpenTranslated()
    {
        var url = Browsed() ?? CurrentUrl;

        if (!string.IsNullOrWhiteSpace(url))
        {
            _dialogs.OpenUrl(PageTranslator.AddressFor(url));
        }
    }

    /// <summary>
    /// What is needed to report an error on what is in front of us: the page,
    /// and the marker to carry into the site's form.
    ///
    /// The marker is the area, the quest, its achievement and the page address:
    /// what the site itself names, and where to open it. The achievement comes from the quest and
    /// not from <c>ChainText</c>, which the banner diverts to say "Path" on a
    /// path and the key on a dungeon. The step's rank used to appear there
    /// first; that is a numbering that exists only in this window, and so it
    /// designated nothing to whoever receives the report.
    ///
    /// The report goes to Papycha, in French: the quest, its series, the
    /// dungeon and the zone are named as Papycha wrote them, not as shown.
    /// </summary>
    public (string Url, string Location)? ErrorReport()
    {
        if (!CanReport)
        {
            return null;
        }

        var quest = _current is { } shown ? _catalog.Source.Quests.FirstOrDefault(q => q.Id == shown.Id) : null;
        var title = quest?.Title
            ?? (_currentDungeon is { } place ? _catalog.Source.Dungeons.FirstOrDefault(d => d.Id == place.Id)?.Title : null)
            ?? QuestTitle;

        return (CurrentUrl!, PapychaReport.Location(ZoneName(), title, quest?.SuccessName ?? _current?.SuccessName, CurrentUrl));
    }

    /// <summary>
    /// The name of the section we are reading under, or empty when the page
    /// has none. The same rule as the list: the one being browsed if the quest
    /// is in it, otherwise the one the catalogue has kept for it. A site
    /// section keeps its French name, for the report.
    /// </summary>
    private string ZoneName() =>
        SectionOfPage() is not { } id
            ? string.Empty
            : _catalog.Source.Sections.FirstOrDefault(s => s.Id == id) is { } section
                ? QuestZoneOrder.DisplayName(section.Name)
                : _tree.NameOf(id);

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

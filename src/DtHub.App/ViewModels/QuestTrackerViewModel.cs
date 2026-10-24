using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;

using Microsoft.Extensions.Logging;

namespace DtHub.App.ViewModels;

/// <summary>A character's box in the team view of "Recommandées": ticked, it counts in the team lists.</summary>
public sealed partial class TeamMember(TrackerCharacter character, bool included) : ObservableObject
{
    public TrackerCharacter Character { get; } = character;

    [ObservableProperty]
    private bool _included = included;
}

/// <summary>
/// The player's tracker laid over the guides window. Thin on purpose: every
/// decision goes through <see cref="QuestTracking"/>. Driven from the UI
/// thread, so no await here drops the UI context.
/// </summary>
public sealed partial class QuestTrackerViewModel(
    IPapychaTracker tracker,
    SettingsService settings,
    ILogger<QuestTrackerViewModel> logger) : ObservableObject
{
    private TrackerState? _state;
    private int? _questId;
    private bool _startedThisQuest;
    private bool _filling;
    private string _rememberedId = string.Empty;
    private HashSet<string> _leftOut = [];

    /// <summary>
    /// Bumped by each write: a read begun before it holds a status the write
    /// is about to change, and must not put it back.
    /// </summary>
    private int _writeVersion;

    /// <summary>Bumped by each <see cref="ShowQuest"/>: a check begun before it is about another quest.</summary>
    private int _shown;

    /// <summary>When a read last put the tracker on screen as linked (Environment.TickCount64).</summary>
    private long _linkedAt;

    /// <summary>
    /// A linked tracker is read again on show at most this often: the page
    /// weighs over a megabyte, and the window is shown many times an hour.
    /// </summary>
    private const long ShowReadInterval = 60_000;

    /// <summary>Gives the tracker cookie, or null when absent. Set by the guides window.</summary>
    public Func<Task<string?>>? CookieSource { get; set; }

    /// <summary>Deletes the tracker cookie. Set by the guides window.</summary>
    public Func<Task>? ForgetCookie { get; set; }

    /// <summary>Raised after every read, so the list can lay the new marks over itself.</summary>
    public event EventHandler? ProgressChanged;

    public ObservableCollection<TrackerCharacter> Characters { get; } = [];

    /// <summary>The team view's boxes, one per character.</summary>
    public ObservableCollection<TeamMember> TeamMembers { get; } = [];

    /// <summary>Raised when a team box is ticked or unticked.</summary>
    public event EventHandler? TeamChanged;

    /// <summary>"Marquer « X » terminée ?", or empty when nothing is offered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFinishOffer))]
    private string _finishOffer = string.Empty;

    public bool ShowsFinishOffer => FinishOffer.Length > 0;

    /// <summary>The quest the offer is about, when there is one.</summary>
    private int? _offeredQuestId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinked))]
    [NotifyPropertyChangedFor(nameof(ShowsLinkButton))]
    [NotifyPropertyChangedFor(nameof(ShowsPitch))]
    [NotifyPropertyChangedFor(nameof(ShowsQuestStatus))]
    [NotifyPropertyChangedFor(nameof(RewardLevel))]
    private TrackerLink _link = TrackerLink.Unknown;

    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RewardLevel))]
    private TrackerCharacter? _selectedCharacter;

    [ObservableProperty]
    private bool _hasSeveralCharacters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsQuestStatus))]
    private TrackerProgress? _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    private QuestStatus _currentStatus;

    [ObservableProperty]
    private bool _highlightsDone;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>A tracker read is under way: up to the client's thirty seconds when the site is down.</summary>
    [ObservableProperty]
    private bool _isReading;

    /// <summary>Reads under way: a show and a write's follow-up can overlap.</summary>
    private int _reads;

    public bool IsLinked => Link == TrackerLink.Linked;

    /// <summary>The level kept in the settings, used when no character is linked (0 = unknown).</summary>
    private int _settingLevel;

    private int _settingBonus;

    /// <summary>The selected character's level when linked, else the remembered one.</summary>
    public int RewardLevel => IsLinked && SelectedCharacter is { } character ? character.Level : _settingLevel;

    /// <summary>What the reward line uses: the pack or nothing, so it always agrees with the box.</summary>
    public int RewardXpBonus => HasBonusPack ? BonusPackPercent : 0;

    /// <summary>The game's Bonus Pack: +150 % XP on quests, the one bonus a player has to tell.</summary>
    public const int BonusPackPercent = 150;

    /// <summary>Any saved bonus reads as the pack: older versions took a free percentage.</summary>
    public bool HasBonusPack => _settingBonus > 0;

    public bool ShowsLinkButton => Link is TrackerLink.NotLinked or TrackerLink.Lost;

    /// <summary>
    /// What the link is for, said to a player who never linked: the button
    /// alone did not tell it was about following quests.
    /// </summary>
    public bool ShowsPitch => Link == TrackerLink.NotLinked;

    /// <summary>A quest is shown and there is a character to track it for.</summary>
    public bool ShowsQuestStatus => IsLinked && _questId is not null && Progress is not null;

    public bool IsDone => CurrentStatus == QuestStatus.Completed;

    public string CurrentStatusLabel => Strings.Get(CurrentStatus switch
    {
        QuestStatus.InProgress => "QuestStatusInProgress",
        QuestStatus.Completed => "QuestStatusCompleted",
        QuestStatus.Blocked => "QuestStatusBlocked",
        QuestStatus.Ignored => "QuestStatusIgnored",
        _ => "QuestStatusNotStarted",
    });

    /// <summary>
    /// Reads the tracker again; a tracker or cookie failure becomes a state.
    /// True when the read was put on screen, false when a write made it stale.
    /// </summary>
    public async Task<bool> RefreshAsync()
    {
        IsReading = ++_reads > 0;

        try
        {
            return await ReadAsync();
        }
        finally
        {
            IsReading = --_reads > 0;
        }
    }

    private async Task<bool> ReadAsync()
    {
        var version = _writeVersion;
        var held = await settings.GetAsync();
        _rememberedId = held.PapychaCharacterId;
        _leftOut = [.. held.PapychaTeamLeftOut];
        var (level, bonus) = (RewardLevel, RewardXpBonus);
        _settingLevel = RewardInput.IsLevel(held.RewardLevel) ? held.RewardLevel : 0;
        _settingBonus = RewardInput.IsBonus(held.RewardXpBonus) ? held.RewardXpBonus : 0;

        // Each one ranks the whole catalogue again in the guides window: told only when it changed.
        if (RewardLevel != level)
        {
            OnPropertyChanged(nameof(RewardLevel));
        }

        if (RewardXpBonus != bonus)
        {
            OnPropertyChanged(nameof(RewardXpBonus));
            OnPropertyChanged(nameof(HasBonusPack));
        }
        var (cookie, cookieFailed) = await ReadCookieAsync();

        if (cookieFailed)
        {
            return Apply(version, TrackerLink.Unreadable, null);
        }

        if (cookie is null)
        {
            return Apply(version, TrackerLink.NotLinked, null);
        }

        TrackerReadResult result;

        try
        {
            result = await tracker.ReadAsync(cookie);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The tracker reports its failures as a result; anything else is
            // a bug that must degrade to "unreadable", not break the window.
            LogReadFailed(exception);

            return Apply(version, TrackerLink.Unreadable, null);
        }

        return result.Outcome switch
        {
            TrackerReadOutcome.Linked when result.State is { } state => Apply(version, TrackerLink.Linked, state),

            // A cookie was sent and refused.
            TrackerReadOutcome.NotLinked => Apply(version, TrackerLink.Lost, null),
            _ => Apply(version, TrackerLink.Unreadable, null),
        };
    }

    /// <summary>
    /// Reads again as the guides window shows, so a status changed elsewhere
    /// (site, phone) comes back. A linked tracker is read at most once a
    /// minute; any other state is read every time, so it can recover.
    /// </summary>
    public Task RefreshOnShowAsync() =>
        Link == TrackerLink.Linked && Environment.TickCount64 - _linkedAt < ShowReadInterval
            ? Task.CompletedTask
            : RefreshAsync();

    /// <summary>
    /// Every ticked character with its progress, at the level the selector shows (a level just saved included).
    /// </summary>
    public IReadOnlyList<(TrackerCharacter Character, TrackerProgress Progress)> TeamProgress() =>
        _state is not { } state
            ? []
            : [.. Characters.Where(c => state.Progress.ContainsKey(c.Id) && !_leftOut.Contains(c.Id)).Select(c => (c, state.Progress[c.Id]))];

    private void OnTeamMemberChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Ids of characters gone from the tracker are dropped with this save.
        _leftOut = [.. TeamMembers.Where(m => !m.Included).Select(m => m.Character.Id)];
        _ = RememberTeamAsync([.. _leftOut]);
        TeamChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RememberTeamAsync(IReadOnlyList<string> leftOut)
    {
        try
        {
            await settings.SetPapychaTeamLeftOutAsync(leftOut);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogRememberFailed(exception);
        }
    }

    private void FillTeam()
    {
        foreach (var member in TeamMembers)
        {
            member.PropertyChanged -= OnTeamMemberChanged;
        }

        TeamMembers.Clear();
        foreach (var character in Characters)
        {
            var member = new TeamMember(character, !_leftOut.Contains(character.Id));
            member.PropertyChanged += OnTeamMemberChanged;
            TeamMembers.Add(member);
        }
    }

    /// <summary>The quest the guides window shows, or null for none.</summary>
    public void ShowQuest(int? questId)
    {
        DismissFinishOffer();
        DismissOthers();
        _questId = questId;
        _shown++;
        _startedThisQuest = false;
        HighlightsDone = false;
        OnPropertyChanged(nameof(ShowsQuestStatus));
        RecomputeStatus();
    }

    /// <summary>The reader moved to a step: may start the quest, may highlight "done".</summary>
    public async Task OnStepAsync(int index, int stepCount, int resumedIndex, bool deliberate)
    {
        HighlightsDone = !IsDone && QuestTracking.IsLastStep(stepCount, index);

        // Only a move the player asked for starts the quest: the step arrows
        // or the step list. Scrolling is reading, and a guide read through was
        // left "in progress" on the account without being played.
        //
        // No quest to start (a dungeon, a path, no character): no read at all,
        // the page weighs 1.3 MB.
        if (!deliberate
            || _startedThisQuest
            || !ShowsQuestStatus
            || !QuestTracking.ShouldStart(CurrentStatus, stepCount, index, resumedIndex))
        {
            return;
        }

        // Once per shown quest, whatever the fresh read says.
        _startedThisQuest = true;

        var shown = _shown;
        var characterId = SelectedCharacter?.Id;

        // The only write the player did not click: judged on a fresh read, so
        // a status set outside DT Hub (site, phone) is never overwritten.
        if (!await RefreshAsync()
            || shown != _shown
            || !IsLinked
            || SelectedCharacter?.Id != characterId
            || !QuestTracking.ShouldStart(CurrentStatus, stepCount, index, resumedIndex))
        {
            return;
        }

        await SetStatusAsync(QuestStatus.InProgress);
    }

    /// <summary>Writes a status for the shown quest.</summary>
    public Task SetStatusAsync(QuestStatus status) =>
        _questId is { } questId ? WriteAsync(questId, status) : Task.CompletedTask;

    /// <summary>
    /// The account's other characters still to finish the quest just marked
    /// done: one click each, or all at once, and ignoring them writes nothing.
    /// </summary>
    public ObservableCollection<TrackerCharacter> OthersToFinish { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAllOthers))]
    private bool _showsOthers;

    public bool ShowsAllOthers => ShowsOthers && OthersToFinish.Count > 1;

    private int? _othersQuestId;

    private void OfferOthers(IReadOnlyList<TrackerCharacter> others, int questId)
    {
        OthersToFinish.Clear();

        foreach (var character in others)
        {
            OthersToFinish.Add(character);
        }

        _othersQuestId = others.Count > 0 ? questId : null;
        ShowsOthers = others.Count > 0;
        OnPropertyChanged(nameof(ShowsAllOthers));
    }

    public void DismissOthers() => OfferOthers([], 0);

    /// <summary>Marks the quest done for one more character.</summary>
    public async Task FinishForAsync(TrackerCharacter character)
    {
        if (_othersQuestId is not { } questId || !OthersToFinish.Contains(character))
        {
            return;
        }

        if (await WriteAsync(questId, QuestStatus.Completed, character))
        {
            OthersToFinish.Remove(character);
            ShowsOthers = OthersToFinish.Count > 0;
            OnPropertyChanged(nameof(ShowsAllOthers));
        }
    }

    /// <summary>Marks the quest done for every character offered, one write after the other.</summary>
    public async Task FinishForAllAsync()
    {
        foreach (var character in OthersToFinish.ToList())
        {
            await FinishForAsync(character);
        }
    }

    /// <summary>
    /// Offers to mark done the quest the player just left with "next quest".
    /// Leaving is not finishing, so nothing is written without "Oui"; the
    /// offer goes as soon as another quest is shown.
    /// </summary>
    public void OfferToFinish(int questId, string title)
    {
        if (!IsLinked || QuestTracking.StatusOf(Progress, questId) == QuestStatus.Completed)
        {
            return;
        }

        _offeredQuestId = questId;
        FinishOffer = Strings.Format("TrackerFinishOffer", title);
    }

    /// <summary>The player said yes: the quest they left is written done.</summary>
    public async Task AcceptFinishOfferAsync()
    {
        if (_offeredQuestId is not { } questId)
        {
            return;
        }

        DismissFinishOffer();
        await WriteAsync(questId, QuestStatus.Completed);
    }

    public void DismissFinishOffer()
    {
        _offeredQuestId = null;
        FinishOffer = string.Empty;
    }

    /// <summary>
    /// Writes a status for a quest. One write at a time: a call made while
    /// another runs is dropped, so two quick clicks write once. The busy flag
    /// is the gate: everything here runs on the UI thread, so the check and
    /// the set cannot interleave. Only the shown quest takes the status at
    /// once; any other waits for the read that follows.
    /// </summary>
    private async Task<bool> WriteAsync(int questId, QuestStatus status, TrackerCharacter? other = null)
    {
        if ((other ?? SelectedCharacter) is not { } character || IsBusy)
        {
            return false;
        }

        _writeVersion++;
        IsBusy = true;

        try
        {
            Notice = string.Empty;

            if (questId == _questId && other is null)
            {
                CurrentStatus = status;
            }

            var (cookie, cookieFailed) = await ReadCookieAsync();

            if (cookie is null)
            {
                // Progress never held the optimistic value, and the shown quest may
                // have changed while the write was in flight.
                RecomputeStatus();
                SetState(cookieFailed ? TrackerLink.Unreadable : TrackerLink.Lost);

                return false;
            }

            TrackerWriteResult result;

            try
            {
                result = await tracker.SetStatusAsync(cookie, character.Id, questId, status);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Same as for reads: the window must survive a bug below it.
                LogWriteFailed(questId, exception);
                result = new TrackerWriteResult(false, null, TrackerReadOutcome.Unreadable);
            }

            if (result.Success)
            {
                if (status == QuestStatus.Completed && other is null && _state is { } state)
                {
                    OfferOthers(QuestTracking.OthersToFinish(state, character.Id, questId), questId);
                }

                _ = RefreshAsync();

                return true;
            }

            // Progress never held the optimistic value, and the shown quest may
            // have changed while the write was in flight.
            RecomputeStatus();

            if (result.After == TrackerReadOutcome.NotLinked)
            {
                SetState(TrackerLink.Lost);
            }
            else
            {
                Notice = string.IsNullOrWhiteSpace(result.Message)
                    ? Strings.Get("TrackerUnreadable")
                    : result.Message;
                LogWriteRefused(questId, Notice);

                // An earlier write may have succeeded: the marks are read
                // again, the refusal stays said.
                _ = RefreshKeepingNoticeAsync(Notice);
            }
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    /// <summary>
    /// Marks many quests done for the selected character, through <see cref="QuestBatch"/>. The busy flag stays up for
    /// the whole run: the tracker has no lock, and a single-row write slipped between the batch's writes brings back
    /// the lost writes. Reads are not put on screen meanwhile. The tracker is read again at the end, without waiting,
    /// so the list shows what the site really kept. Null when nothing was started.
    /// </summary>
    public async Task<QuestBatchResult?> MarkDoneAsync(
        IReadOnlyList<int> questIds, IProgress<QuestBatchProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questIds);

        if (SelectedCharacter is not { } character || IsBusy || questIds.Count == 0)
        {
            return null;
        }

        _writeVersion++;
        IsBusy = true;
        QuestBatchResult? result = null;

        try
        {
            Notice = string.Empty;
            var (cookie, cookieFailed) = await ReadCookieAsync();

            if (cookie is null)
            {
                SetState(cookieFailed ? TrackerLink.Unreadable : TrackerLink.Lost);

                return null;
            }

            try
            {
                result = await QuestBatch.RunAsync(
                    tracker, cookie, character.Id, questIds, QuestStatus.Completed, progress, cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The batch reports its failures as a result; anything else is a bug that must not break the window.
                LogWriteFailed(0, exception);
                result = new QuestBatchResult(
                    [], [], questIds, exception is OperationCanceledException ? null : TrackerReadOutcome.Unreadable,
                    exception is OperationCanceledException);
            }
        }
        finally
        {
            IsBusy = false;
        }

        _ = RefreshAsync();

        return result;
    }

    /// <summary>
    /// Applies the level box. Linked, the level is saved on the player's
    /// Papycha character and only shown once the site took it; a refusal keeps
    /// the old level and says why. Not linked, it is kept in the settings.
    /// True when the level changed.
    /// </summary>
    public async Task<bool> SetRewardLevelAsync(string text)
    {
        if (!RewardInput.TryLevel(text, out var level) || level == RewardLevel)
        {
            OnPropertyChanged(nameof(RewardLevel));

            return false;
        }

        if (!IsLinked || SelectedCharacter is not { } character)
        {
            _settingLevel = level;
            OnPropertyChanged(nameof(RewardLevel));
            await SaveSettingAsync(() => settings.SetRewardLevelAsync(level));

            return true;
        }

        if (IsBusy)
        {
            OnPropertyChanged(nameof(RewardLevel));

            return false;
        }

        _writeVersion++;
        IsBusy = true;

        try
        {
            Notice = string.Empty;
            var (cookie, cookieFailed) = await ReadCookieAsync();

            if (cookie is null)
            {
                SetState(cookieFailed ? TrackerLink.Unreadable : TrackerLink.Lost);

                return false;
            }

            var updated = character with { Level = level };
            TrackerWriteResult result;

            try
            {
                result = await tracker.SaveCharacterAsync(cookie, updated);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogWriteFailed(0, exception);
                result = new TrackerWriteResult(false, null, TrackerReadOutcome.Unreadable);
            }

            if (result.Success)
            {
                var index = Characters.IndexOf(character);
                // Read before the replace: the selector drops a replaced selected item and pushes null back.
                var wasSelected = SelectedCharacter?.Id == character.Id;

                if (index >= 0)
                {
                    Characters[index] = updated;
                }

                if (wasSelected)
                {
                    SelectedCharacter = updated;
                }

                return true;
            }

            if (result.After == TrackerReadOutcome.NotLinked)
            {
                SetState(TrackerLink.Lost);
            }
            else
            {
                Notice = string.IsNullOrWhiteSpace(result.Message)
                    ? Strings.Get("TrackerUnreadable")
                    : result.Message;
                LogWriteRefused(0, Notice);
            }

            OnPropertyChanged(nameof(RewardLevel));
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    /// <summary>Applies the Bonus Pack switch (0 to 1000 percent); a refused value puts the old one back.</summary>
    public async Task<bool> SetRewardXpBonusAsync(int percent)
    {
        if (!RewardInput.IsBonus(percent) || percent == _settingBonus)
        {
            OnPropertyChanged(nameof(HasBonusPack));

            return false;
        }

        _settingBonus = percent;
        OnPropertyChanged(nameof(RewardXpBonus));
        OnPropertyChanged(nameof(HasBonusPack));
        await SaveSettingAsync(() => settings.SetRewardXpBonusAsync(percent));

        return true;
    }

    /// <summary>Forgets the cookie and drops everything the tracker gave.</summary>
    public async Task UnlinkAsync()
    {
        if (ForgetCookie is { } forget)
        {
            try
            {
                await forget();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The WebView may be asleep; the state below is still dropped.
                LogForgetFailed(exception);
            }
        }

        _writeVersion++;
        SetState(TrackerLink.NotLinked);
    }

    private async Task RefreshKeepingNoticeAsync(string notice)
    {
        if (await RefreshAsync() && IsLinked)
        {
            Notice = notice;
        }
    }

    partial void OnCurrentStatusChanged(QuestStatus value)
    {
        if (value == QuestStatus.Completed)
        {
            HighlightsDone = false;
        }
    }

    /// <summary>Picks a character by its id, as the site's tracker did; an unknown id is ignored.</summary>
    public void PickCharacter(string id)
    {
        if (Characters.FirstOrDefault(c => c.Id == id) is { } character && character != SelectedCharacter)
        {
            SelectedCharacter = character;
        }
    }

    partial void OnSelectedCharacterChanged(TrackerCharacter? value)
    {
        if (value is null || _state is null || _filling)
        {
            return;
        }

        _rememberedId = value.Id;
        _ = RememberAsync(value.Id);
        ShowProgressOf(value);
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveSettingAsync(Func<Task> save)
    {
        try
        {
            await save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogRememberFailed(exception);
        }
    }

    private async Task RememberAsync(string id)
    {
        try
        {
            await settings.SetPapychaCharacterAsync(id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogRememberFailed(exception);
        }
    }

    /// <summary>The cookie, or null with whether the null came from a failure to read it.</summary>
    private async Task<(string? Cookie, bool Failed)> ReadCookieAsync()
    {
        if (CookieSource is not { } source)
        {
            return (null, false);
        }

        try
        {
            var cookie = await source();

            return (string.IsNullOrEmpty(cookie) ? null : cookie, false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A hidden window or sleeping WebView throws here: unreadable,
            // not unlinked, so the player is not told to link again.
            LogCookieFailed(exception);

            return (null, true);
        }
    }

    /// <summary>
    /// Puts a read's outcome on screen, unless a write began after the read
    /// did (its status is newer than what the read saw). True when it did.
    /// </summary>
    private bool Apply(int version, TrackerLink link, TrackerState? state)
    {
        if (version != _writeVersion || IsBusy)
        {
            return false;
        }

        if (link == TrackerLink.Linked)
        {
            _linkedAt = Environment.TickCount64;
        }

        _state = state;
        Link = link;
        Notice = NoticeFor(link);

        _filling = true;
        Characters.Clear();

        if (state is not null)
        {
            foreach (var character in state.Characters)
            {
                Characters.Add(character);
            }
        }

        HasSeveralCharacters = Characters.Count > 1;
        FillTeam();

        var picked = state is null
            ? null
            : QuestTracking.PickCharacter(state.Characters, _rememberedId, state.ActiveCharacterId);

        SelectedCharacter = picked;
        _filling = false;

        ShowProgressOf(picked);
        LogLink(link);
        ProgressChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    private static string NoticeFor(TrackerLink link) => link switch
    {
        TrackerLink.Lost => Strings.Get("TrackerLost"),
        TrackerLink.Unreadable => Strings.Get("TrackerUnreadable"),
        _ => string.Empty,
    };

    private void SetState(TrackerLink link)
    {
        _state = null;
        Link = link;
        Notice = NoticeFor(link);
        Characters.Clear();
        HasSeveralCharacters = false;
        FillTeam();
        SelectedCharacter = null;
        ShowProgressOf(null);
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowProgressOf(TrackerCharacter? character)
    {
        Progress = character is not null && _state?.Progress.TryGetValue(character.Id, out var progress) == true
            ? progress
            : null;
        RecomputeStatus();
    }

    private void RecomputeStatus()
    {
        CurrentStatus = _questId is { } id ? QuestTracking.StatusOf(Progress, id) : QuestStatus.NotStarted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Suivi Papycha : état {link}.")]
    private partial void LogLink(TrackerLink link);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suivi Papycha : lecture en échec.")]
    private partial void LogReadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suivi Papycha : le cookie n'a pas pu être lu.")]
    private partial void LogCookieFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suivi Papycha : écriture de la quête {questId} en échec.")]
    private partial void LogWriteFailed(int questId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suivi Papycha : écriture de la quête {questId} refusée ({reason}).")]
    private partial void LogWriteRefused(int questId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suivi Papycha : le cookie n'a pas pu être effacé.")]
    private partial void LogForgetFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suivi Papycha : le personnage choisi n'a pas pu être retenu.")]
    private partial void LogRememberFailed(Exception exception);
}

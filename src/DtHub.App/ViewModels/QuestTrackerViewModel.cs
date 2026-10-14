using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;

using Microsoft.Extensions.Logging;

namespace DtHub.App.ViewModels;

/// <summary>Where the link with the player's Papycha tracker stands.</summary>
public enum TrackerLink
{
    /// <summary>Not read yet.</summary>
    Unknown,

    /// <summary>No tracker cookie: the player never linked, or unlinked.</summary>
    NotLinked,

    Linked,

    /// <summary>The cookie is there but the site no longer accepts it (phrase renewed).</summary>
    Lost,

    /// <summary>Network down, site changed, or the cookie could not be read.</summary>
    Unreadable,
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinked))]
    [NotifyPropertyChangedFor(nameof(ShowsLinkButton))]
    [NotifyPropertyChangedFor(nameof(ShowsQuestStatus))]
    private TrackerLink _link = TrackerLink.Unknown;

    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
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

    public bool IsLinked => Link == TrackerLink.Linked;

    public bool ShowsLinkButton => Link is TrackerLink.NotLinked or TrackerLink.Lost;

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
        var version = _writeVersion;
        _rememberedId = (await settings.GetAsync()).PapychaCharacterId;
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

    /// <summary>The quest the guides window shows, or null for none.</summary>
    public void ShowQuest(int? questId)
    {
        _questId = questId;
        _shown++;
        _startedThisQuest = false;
        HighlightsDone = false;
        OnPropertyChanged(nameof(ShowsQuestStatus));
        RecomputeStatus();
    }

    /// <summary>The reader moved to a step: may start the quest, may highlight "done".</summary>
    public async Task OnStepAsync(int index, int stepCount, int resumedIndex)
    {
        HighlightsDone = !IsDone && QuestTracking.IsLastStep(stepCount, index);

        if (_startedThisQuest
            || !IsLinked
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

    /// <summary>
    /// Writes a status for the shown quest. One write at a time: a call made
    /// while another runs is dropped, so two quick clicks write once. The
    /// busy flag is the gate: everything here runs on the UI thread, so the
    /// check and the set cannot interleave.
    /// </summary>
    public async Task SetStatusAsync(QuestStatus status)
    {
        if (_questId is not { } questId || SelectedCharacter is not { } character || IsBusy)
        {
            return;
        }

        _writeVersion++;
        IsBusy = true;

        try
        {
            Notice = string.Empty;
            CurrentStatus = status;

            var (cookie, cookieFailed) = await ReadCookieAsync();

            if (cookie is null)
            {
                // Progress never held the optimistic value, and the shown quest may
                // have changed while the write was in flight.
                RecomputeStatus();
                SetState(cookieFailed ? TrackerLink.Unreadable : TrackerLink.Lost);

                return;
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
                _ = RefreshAsync();

                return;
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

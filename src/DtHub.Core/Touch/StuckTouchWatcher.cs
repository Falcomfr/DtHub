using DtHub.Core.Adb;
using DtHub.Core.Windows;

namespace DtHub.Core.Touch;

/// <summary>An open game window, as the watcher needs to know it.</summary>
/// <param name="Key">The account's key, to find its row.</param>
/// <param name="Serial">The phone it runs on.</param>
/// <param name="DisplayId">Its Android virtual display.</param>
/// <param name="Window">Its scrcpy window.</param>
public sealed record WatchedDisplay(string Key, string Serial, int DisplayId, nint Window);

/// <summary>What became of fingers found stuck.</summary>
public enum StuckTouchOutcome
{
    /// <summary>Released by the posted button up. Nothing to show.</summary>
    Released,

    /// <summary>
    /// Still down after the release: the player is asked to click the map
    /// a few times, which is what freed the account the first time.
    /// </summary>
    NeedsClicks,

    /// <summary>Free again after a <see cref="NeedsClicks" />.</summary>
    Cleared,
}

/// <summary>One account's stuck fingers, and what came of them.</summary>
public sealed record StuckTouchReport(string Key, StuckTouchOutcome Outcome, int Fingers);

/// <summary>
/// Finds fingers left down on a game's display, and lifts them.
///
/// **The fault.** scrcpy turns Ctrl+click into a two finger pinch, and every
/// DT Hub shortcut holds Ctrl. Measured on 2026-09-27: after a Ctrl+Tab, the
/// new window knows Ctrl is held, and once a release went missing Android
/// kept two fingers down for good. Every later click was a third finger, and
/// the map ignored it. What sets the release off was not found; this watcher
/// does not need it.
///
/// **The repair, and the one it must never be.** A button up posted to the
/// scrcpy window makes scrcpy lift its own fingers: the game then receives
/// the end of the very gesture it is waiting on. Measured on a held click
/// and on a held Ctrl+click, both freed at once. Cancelling the gesture on
/// the Android side, with <c>input motionevent CANCEL</c>, clears Android
/// but not DOFUS Touch, which ignores the cancel and keeps its finger for as
/// long as the game runs: no tap can lift it, and only restarting the game
/// did. That was measured too, on a live account.
/// </summary>
public sealed class StuckTouchWatcher
{
    private readonly IAdbClient _adb;
    private readonly IWindowController _windows;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly Dictionary<string, Stuck> _stuck = [];

    public StuckTouchWatcher(
        IAdbClient adb,
        IWindowController windows,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentNullException.ThrowIfNull(windows);

        _adb = adb;
        _windows = windows;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>Time between two readings of each phone.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long fingers must stay down, mouse buttons up, before they are
    /// taken for stuck. Long enough for a tap, a double tap and the gap
    /// between two clicks of a drag.
    /// </summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Time given to the release before reading again.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Raised on a repair, a request to click, and the all clear.</summary>
    public event EventHandler<StuckTouchReport>? Reported;

    /// <summary>
    /// Watches until cancelled. A phone that fails to answer is skipped for
    /// that round, never the loop.
    /// </summary>
    public async Task RunAsync(Func<IReadOnlyList<WatchedDisplay>> displays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(displays);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(displays(), cancellationToken).ConfigureAwait(false);
                await _delay(Interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The application is closing: the one way out of the loop.
                return;
            }
        }
    }

    /// <summary>One round: every phone with an open game window, read once.</summary>
    public async Task CheckAsync(IReadOnlyList<WatchedDisplay> displays, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displays);

        // A window that closed takes its state with it.
        foreach (var gone in _stuck.Keys.Where(k => displays.All(d => d.Key != k)).ToList())
        {
            _stuck.Remove(gone);
        }

        foreach (var phone in displays.GroupBy(d => d.Serial, StringComparer.Ordinal))
        {
            if (await ReadAsync(phone.Key, cancellationToken).ConfigureAwait(false) is not { } touches)
            {
                continue;
            }

            foreach (var display in phone)
            {
                await StepAsync(display, touches.GetValueOrDefault(display.DisplayId), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task StepAsync(WatchedDisplay display, int fingers, CancellationToken cancellationToken)
    {
        if (fingers == 0)
        {
            if (_stuck.Remove(display.Key, out var was) && was.Asked)
            {
                Reported?.Invoke(this, new StuckTouchReport(display.Key, StuckTouchOutcome.Cleared, 0));
            }

            return;
        }

        var stuck = _stuck.TryGetValue(display.Key, out var known) ? known : _stuck[display.Key] = new Stuck();

        // A button held is a drag, or a press held on purpose: never cut it,
        // and count the patience again from its end.
        if (_windows.IsMouseButtonDown())
        {
            stuck.Since = null;
            return;
        }

        stuck.Since ??= _clock();

        if (stuck.Tried || _clock() - stuck.Since < Patience)
        {
            return;
        }

        stuck.Tried = true;

        _windows.ReleaseMouseButton(display.Window);

        await _delay(Settle, cancellationToken).ConfigureAwait(false);

        var after = await ReadAsync(display.Serial, cancellationToken).ConfigureAwait(false);

        if (after is not null && after.GetValueOrDefault(display.DisplayId) == 0)
        {
            _stuck.Remove(display.Key);
            Reported?.Invoke(this, new StuckTouchReport(display.Key, StuckTouchOutcome.Released, fingers));
            return;
        }

        // No second try and no click in the player's stead: a click lands
        // in the game. The row asks for what freed the account by hand.
        stuck.Asked = true;
        Reported?.Invoke(this, new StuckTouchReport(display.Key, StuckTouchOutcome.NeedsClicks, fingers));
    }

    private async Task<IReadOnlyDictionary<int, int>?> ReadAsync(string serial, CancellationToken cancellationToken)
    {
        try
        {
            var dump = await _adb
                .ShellAsync(serial, TouchStateReader.Command, TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);

            return TouchStateReader.Read(dump);
        }
        catch (AdbException)
        {
            // The phone is gone or busy: the device sweep says so, not us.
            return null;
        }
    }

    private sealed class Stuck
    {
        public DateTimeOffset? Since { get; set; }

        public bool Tried { get; set; }

        public bool Asked { get; set; }
    }
}

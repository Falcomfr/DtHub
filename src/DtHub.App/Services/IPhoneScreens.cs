using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core.Dofus;
using DtHub.Core.IPhone;
using DtHub.Core.Localization;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Infrastructure.IPhone;

using Microsoft.Extensions.Logging;

namespace DtHub.App.Services;

/// <summary>
/// The iPhones of the device list and their open screens, one window per
/// iOS account (D227), each living as an Android account's window
/// (<see cref="IosWindowLife"/>). Used on the UI thread only.
/// </summary>
public sealed partial class IPhoneScreens(
    PyMobileDevice tools, WebViewEnvironment engine, SettingsService settings, ILogger<IPhoneScreens> logger)
{
    // usbmux list starts Python: not at every sweep of the list.
    private static readonly TimeSpan ListEvery = TimeSpan.FromSeconds(10);

    // A close or a quit waits this long at most for the device to end its game.
    private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, IosGameWindow> _windows = new(StringComparer.Ordinal);
    private readonly HashSet<string> _opening = new(StringComparer.Ordinal);

    // Kept past the window: a reopening that failed removes it, its row still says why.
    private readonly Dictionary<string, string> _problems = new(StringComparer.Ordinal);

    private DateTime _listed = DateTime.MinValue;
    private bool _listing;
    private bool _stopping;

    /// <summary>The iOS accounts' windows, alive or not: <see cref="IosGameWindow.IsAlive"/> tells.</summary>
    public IReadOnlyList<IosGameWindow> Windows => [.. _windows.Values];

    /// <summary>The devices of the last search.</summary>
    public IReadOnlyList<IPhoneDevice> Listed { get; private set; } = [];

    /// <summary>The last search failed: <see cref="Listed"/> is empty for want of an answer.</summary>
    public bool ListFailed { get; private set; }

    /// <summary>A window was shown for this key, first time or again: its handle is new.</summary>
    public event EventHandler<string>? Opened;

    /// <summary>The account's window ended, and why.</summary>
    public event EventHandler<(string Key, IosEnd End)>? Ended;

    /// <summary>The account's picture changed shape.</summary>
    public event EventHandler<string>? AspectChanged;

    /// <summary>A screen opened, closed or changed state: the arranging buttons count them.</summary>
    public event EventHandler? Changed;

    /// <summary>Where the account's window is in its life, <c>null</c> without one.</summary>
    public IosWindowState? StateOf(string key) => _windows.TryGetValue(key, out var window) ? window.Life.State : null;

    /// <summary>The window shows, and DOFUS Touch was not found on the device.</summary>
    public bool IsGameMissing(string key) => _windows.TryGetValue(key, out var window) && window.GameMissing;

    /// <summary>What went wrong with the account's window or its game, said to the player; <c>null</c> when nothing.</summary>
    public string? ProblemOf(string key) => _problems.GetValueOrDefault(key);

    /// <summary>Out of maximized, before an arrangement moves them: Windows would keep them flagged so.</summary>
    public void Unmaximize()
    {
        foreach (var entry in Shown.Where(w => w.Window.WindowState == System.Windows.WindowState.Maximized))
        {
            entry.Window.WindowState = System.Windows.WindowState.Normal;
        }
    }

    /// <summary>
    /// The account's window goes into the tabbed frame, or leaves it: held,
    /// the frame alone sizes it, and a window opened again for it is born
    /// off screen for the frame (<see cref="IPhoneMirrorWindow.ShowForFrame"/>).
    /// </summary>
    public void SetDocked(string key, bool docked)
    {
        if (!_windows.TryGetValue(key, out var entry))
        {
            return;
        }

        entry.Docked = docked;
        entry.Window.Docked = docked;
    }

    /// <summary>The account's docked window lost the keyboard: what the page holds down is let go.</summary>
    public void Blur(string key)
    {
        if (_windows.TryGetValue(key, out var entry))
        {
            entry.Window.SendBlur();
        }
    }

    /// <summary>A window shows, or waits for its device or its next attempt: DT Hub stays.</summary>
    public bool AnyKeepsAppAlive => _windows.Values.Any(w => w.Life.KeepsAppAlive);

    /// <summary>The account's window shows the game now: its playtime runs.</summary>
    public bool CountsPlaytime(string key) => _windows.TryGetValue(key, out var window) && window.Life.CountsPlaytime;

    /// <summary>Looks for iPhones, once the iPhone tools were set up.</summary>
    public async Task RefreshAsync()
    {
        if (_listing || !tools.IsInstalled || DateTime.UtcNow - _listed < ListEvery)
        {
            return;
        }

        _listing = true;

        try
        {
            Show(await tools.ListAsync(CancellationToken.None).ConfigureAwait(true));
        }
        catch (IPhoneException exception)
        {
            // Apple Devices closed, say: the list just shows no iPhone. Not a sign
            // that one was unplugged, so the open screens are left alone.
            LogListFailed(exception.Message);
            Listed = [];
            ListFailed = true;
        }
        finally
        {
            _listed = DateTime.UtcNow;
            _listing = false;
        }
    }

    /// <summary>Looks for iPhones now, past the wait between searches: one in flight may predate a plug or an unplug.</summary>
    public async Task ListNowAsync()
    {
        while (_listing)
        {
            await Task.Delay(200).ConfigureAwait(true);
        }

        _listed = DateTime.MinValue;
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Shows the iPhones Apple's USB service listed. An open screen whose
    /// iPhone is gone stops its tool; back in the list, it opens again
    /// where it was.
    /// </summary>
    public void Show(IReadOnlyList<IPhoneDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        foreach (var entry in Shown.ToList())
        {
            var device = devices.FirstOrDefault(d => d.Udid == entry.Device.Udid);

            if (device is null)
            {
                entry.Window.Unplugged();
            }

            // Also a window that saw its tool wait for a tunnel, the iPhone still listed.
            if (entry.Window.IsUnplugged && entry.Life.State is not (IosWindowState.Unplugged or IosWindowState.Closed))
            {
                entry.Life.Unplug();
                Changed?.Invoke(this, EventArgs.Empty);
            }

            if (device is not null && entry.Window.IsUnplugged)
            {
                if (entry.Life.State == IosWindowState.Closed)
                {
                    // Given up, its window still shown: back from an unplug, a fresh start, as before.
                    entry.Life = new IosWindowLife();
                    entry.Served = false;
                }

                _ = ReopenAsync(entry, device);
            }
        }

        // The list keeps an account row while its window waits for the device.
        Listed = devices;
        ListFailed = false;
    }

    /// <summary>Opens the account's screen, or brings it forward.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task OpenAsync(DofusInstance account, IPhoneDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(device);

        // Every way in (row, startup, team, iPhone window): below iOS 27 the tool is refused.
        if (DeviceGroupViewModel.TooOldHelp(device) is { } tooOld)
        {
            throw new IPhoneException(tooOld);
        }

        if (_opening.Contains(account.Key))
        {
            return;
        }

        SetProblem(account.Key, null);

        if (_windows.TryGetValue(account.Key, out var shown))
        {
            switch (shown.Life.State)
            {
                case IosWindowState.Failed:
                    await ReopenAsync(shown, device).ConfigureAwait(true);
                    return;

                case IosWindowState.Closed:
                    // Given up, its window saying why: a fresh start.
                    shown.Window.Close();
                    break;

                default:
                    shown.Window.Activate();
                    return;
            }
        }

        if (!_opening.Add(account.Key))
        {
            return;
        }

        try
        {
            await ShowAsync(new IosGameWindow(account.Key, device, account.IosBundleId), place: null, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _opening.Remove(account.Key);
        }
    }

    /// <summary>Closes the account's window; <paramref name="byHand"/> says the player asked for it.</summary>
    public Task CloseAsync(string key, bool byHand)
    {
        if (_windows.Remove(key, out var entry))
        {
            // A tab's cross as DT Hub quits is not the player's close (D207).
            IosEnd? end = entry.Life.State == IosWindowState.Closed ? null : entry.Life.Close(byHand && !_stopping);

            // Being opened again, its old window is already closed and the new one
            // not shown yet: the closed life stops the opening (ShowAsync).
            if (!_opening.Contains(key))
            {
                entry.Window.Close();
            }

            SetOpen(false);

            if (end is { } ended)
            {
                Ended?.Invoke(this, (key, ended));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Ends the account's game, when its device is listed and the game known:
    /// an unplugged device is not tried. A failure or a device too slow is
    /// logged, never thrown.
    /// </summary>
    public async Task StopGameAsync(string key)
    {
        if (Controlled(key) is not { } entry)
        {
            return;
        }

        using var limit = new CancellationTokenSource(StopLimit);

        try
        {
            await entry.Game!.StopAsync(entry.BundleId!, limit.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Also the quit's: nothing may escape it. A device too slow is a cancel here.
            LogGameStopFailed(exception, entry.Device.Name);
        }
    }

    /// <summary>
    /// DT Hub quits: every game shown ends, when "Stop the game on close" asks
    /// for it, while the streams still run. Each waits <see cref="StopLimit"/> at most.
    /// </summary>
    public async Task StopAllGamesAsync()
    {
        try
        {
            if ((await settings.GetAsync(CancellationToken.None).ConfigureAwait(true)).StopAppOnClose)
            {
                await Task.WhenAll(_windows.Keys.ToList().Select(StopGameAsync)).ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Run on every quit, Android players' too: the quit goes on.
            LogStopAllFailed(exception);
        }
    }

    /// <summary>Stops then starts the account's game again, its window kept.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task RestartGameAsync(string key)
    {
        var entry = Controlled(key) ?? throw new IPhoneException(Strings.Get("IosGameUnreachable"));

        await entry.Game!.StopAsync(entry.BundleId!, CancellationToken.None).ConfigureAwait(true);
        await entry.Game.LaunchAsync(entry.BundleId!, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>The account's window whose game can be told what to do: device listed, game found.</summary>
    private IosGameWindow? Controlled(string key) =>
        _windows.TryGetValue(key, out var entry)
        && entry is { Game: not null, BundleId: not null }
        && Listed.Any(d => d.Udid == entry.Device.Udid)
            ? entry
            : null;

    /// <summary>DT Hub quits: the windows it closes are not closed by the player (D207).</summary>
    public void MarkAllStopping() => _stopping = true;

    /// <summary>Starts the device's stream and shows it, in <paramref name="place"/> when given.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    private async Task ShowAsync(IosGameWindow entry, System.Windows.Rect? place, CancellationToken cancellationToken)
    {
        var preferences = await settings.GetAsync(cancellationToken).ConfigureAwait(true);
        var (port, controlPort) = PyMobileDevice.FreePorts();
        var device = entry.Device;
        var session = await tools.StartMirrorAsync(device, port, preferences.AudioEnabled, controlPort, libusb0Path: null, cancellationToken).ConfigureAwait(true);

        if (entry.Life.State == IosWindowState.Closed)
        {
            // Closed while the tool started: it goes, nothing is shown.
            await session.DisposeAsync().ConfigureAwait(true);
            return;
        }

        IPhoneMirrorWindow window;

        try
        {
            window = new IPhoneMirrorWindow(device, port, session, engine, dark: preferences.TurnScreenOff);

            // A docked account's is placed by ShowForFrame below.
            if (!entry.Docked && place is { IsEmpty: false } rect)
            {
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                (window.Left, window.Top, window.Width, window.Height) = (rect.Left, rect.Top, rect.Width, rect.Height);
            }
        }
        catch
        {
            // Without its window, nothing would ever stop the stream.
            await session.DisposeAsync().ConfigureAwait(true);
            throw;
        }

        window.Served += async (_, _) =>
        {
            if (IsCurrent(entry, window))
            {
                entry.Served = true;
                entry.Life.Opened();
                Changed?.Invoke(this, EventArgs.Empty);

                if (entry.Life.State == IosWindowState.Open)
                {
                    await StartGameAsync(entry).ConfigureAwait(true);
                }
            }
        };
        window.Stopped += async (_, _) => await OnStoppedAsync(entry, window).ConfigureAwait(true);
        window.AspectChanged += (_, _) =>
        {
            if (IsCurrent(entry, window))
            {
                AspectChanged?.Invoke(this, entry.Key);
            }
        };
        var closingHeld = false;
        window.Closing += (_, e) =>
        {
            // Held once: the close that follows the game's stop goes through.
            if (closingHeld || !HoldsClosing(entry, window))
            {
                return;
            }

            var preferences = settings.GetAsync(CancellationToken.None);

            // Read from memory, the setting is known now: off, nothing to hold.
            if (preferences.IsCompletedSuccessfully && !preferences.Result.StopAppOnClose)
            {
                return;
            }

            e.Cancel = closingHeld = true;
            _ = StopThenCloseAsync(entry, window, preferences);
        };
        window.Closed += (_, _) => OnClosed(entry, window);
        // Opened again for a tabbed account: off screen, not activated, the launcher docks it on Opened.
        if (entry.Docked)
        {
            window.ShowForFrame(place ?? System.Windows.Rect.Empty);
        }
        else
        {
            window.Show();
        }

        entry.Window = window;
        entry.Game = new IosGameControl(controlPort);
        _windows[entry.Key] = entry;
        SetOpen(true);
        Opened?.Invoke(this, entry.Key);
    }

    /// <summary>
    /// Opens DOFUS Touch once the stream serves, found on the device the
    /// first time. A failure is logged, never thrown: this runs from an
    /// event handler, and the window stays.
    /// </summary>
    private async Task StartGameAsync(IosGameWindow entry)
    {
        if (entry.Game is not { } game)
        {
            return;
        }

        try
        {
            if (entry.BundleId is null)
            {
                entry.BundleId = await game.FindDofusAsync(CancellationToken.None).ConfigureAwait(true);
                entry.GameMissing = entry.BundleId is null;
                Changed?.Invoke(this, EventArgs.Empty);

                if (entry.BundleId is null)
                {
                    LogGameMissing(entry.Device.Name);
                    return;
                }

                await settings.SetIosBundleIdAsync(entry.Key, entry.BundleId).ConfigureAwait(true);
            }

            if (!await game.IsRunningAsync(entry.BundleId, CancellationToken.None).ConfigureAwait(true))
            {
                await game.LaunchAsync(entry.BundleId, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Also the bundle id that could not be saved.
            LogGameFailed(exception, entry.Device.Name);
            SetProblem(entry.Key, Strings.Get("IPhoneGameFailed"));
        }
    }

    /// <summary>
    /// The stream stopped or never served: opened again as Android's
    /// recovery does, unless the device was unplugged.
    /// </summary>
    private async Task OnStoppedAsync(IosGameWindow entry, IPhoneMirrorWindow window)
    {
        // A tool ended by an unplug is no failure: a fresh list tells.
        await ListNowAsync().ConfigureAwait(true);

        if (window.IsUnplugged
            || _opening.Contains(entry.Key)
            || !IsCurrent(entry, window)
            || entry.Life.State is not (IosWindowState.Open or IosWindowState.Opening))
        {
            return;
        }

        if (DateTime.UtcNow - entry.LastFailure > SessionRecovery.Forget)
        {
            entry.Life.Forget();
        }

        entry.LastFailure = DateTime.UtcNow;

        // As Android: a stream that never served is not tried again, its window says why.
        if (entry.Life.Fail(entry.Served ? SessionRecovery.MaxAttempts : 0) is { } end)
        {
            LogGaveUp(entry.Device.Name, entry.Life.Attempts);
            Changed?.Invoke(this, EventArgs.Empty);
            Ended?.Invoke(this, (entry.Key, end));
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await Task.Delay(SessionRecovery.DelayFor(entry.Life.Attempts)).ConfigureAwait(true);

        if (IsCurrent(entry, window) && entry.Life.State == IosWindowState.Failed)
        {
            LogReopening(entry.Device.Name, entry.Life.Attempts);
            await ReopenAsync(entry, entry.Device).ConfigureAwait(true);
        }
    }

    /// <summary>The device is back, or its stream failed: a fresh tool and window, in the old one's place.</summary>
    private async Task ReopenAsync(IosGameWindow entry, IPhoneDevice device)
    {
        if (!_opening.Add(entry.Key))
        {
            return;
        }

        var old = entry.Window;
        // A docked window's own bounds are the frame's: where it stood free instead.
        var place = old.Docked ? old.FreeBounds : old.RestoreBounds;
        entry.Device = device;
        entry.Life.Replugged();

        // Closed first, as before: two tools on one device at once is not tried.
        // Its Closed sees the reopening and ends nothing.
        old.Close();
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            await ShowAsync(entry, place, CancellationToken.None).ConfigureAwait(true);
        }
        catch (IPhoneException exception)
        {
            // The tool could not start: trying again would not help.
            LogReopenFailed(device.Name, exception.Message);

            SetProblem(entry.Key, exception.Message);

            // Closed meanwhile from DT Hub: already ended.
            if (entry.Life.Fail(0) is { } end)
            {
                _windows.Remove(entry.Key);
                SetOpen(false);
                Ended?.Invoke(this, (entry.Key, end));
            }
        }
        finally
        {
            _opening.Remove(entry.Key);
        }
    }

    /// <summary>
    /// The player's cross on a window showing the game: its first close is
    /// held while "Stop the game on close" reaches the game, the stream still
    /// running, as Android's cross stops its game. DT Hub's own closes, a
    /// reopening and quitting go straight through: they stop the game
    /// themselves, or cannot.
    /// </summary>
    private bool HoldsClosing(IosGameWindow entry, IPhoneMirrorWindow window) =>
        !_stopping
        && !_opening.Contains(entry.Key)
        && IsCurrent(entry, window)
        && entry.Life.State == IosWindowState.Open
        && Controlled(entry.Key) is not null;

    private async Task StopThenCloseAsync(IosGameWindow entry, IPhoneMirrorWindow window, Task<AppSettingsDocument> preferences)
    {
        // After the Closing that held it has returned: WPF refuses a Close from inside it.
        await Task.Yield();

        try
        {
            if ((await preferences.ConfigureAwait(true)).StopAppOnClose)
            {
                await StopGameAsync(entry.Key).ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The settings could not be read: the window still closes.
            LogGameStopFailed(exception, entry.Device.Name);
        }

        try
        {
            // Closed meanwhile (a second cross, a reopening): nothing left to close.
            if (IsCurrent(entry, window))
            {
                window.Close();
            }
        }
        catch (InvalidOperationException exception)
        {
            // Never held open in silence: the player's next cross goes through.
            LogCloseFailed(exception, entry.Device.Name);
        }
    }

    private void OnClosed(IosGameWindow entry, IPhoneMirrorWindow window)
    {
        if (!IsCurrent(entry, window) || _opening.Contains(entry.Key))
        {
            return;
        }

        _windows.Remove(entry.Key);

        if (entry.Life.State != IosWindowState.Closed)
        {
            Ended?.Invoke(this, (entry.Key, entry.Life.Close(byHand: !_stopping)));
        }

        SetOpen(false);
    }

    private bool IsCurrent(IosGameWindow entry, IPhoneMirrorWindow window) =>
        _windows.TryGetValue(entry.Key, out var current) && current == entry && current.Window == window;

    /// <summary>The accounts whose window is on screen: one being opened again has none for a moment.</summary>
    private IEnumerable<IosGameWindow> Shown => _windows.Values.Where(w => !_opening.Contains(w.Key));

    /// <summary>"Turn the phone's screen off" switched: dims or lights every iPhone shown.</summary>
    public void SetDark(bool dark)
    {
        foreach (var entry in Shown)
        {
            entry.Window.SetDark(dark);
        }
    }

    /// <summary>Ctrl+E on an iPhone's window: dims or lights that iPhone.</summary>
    public bool ToggleDark(nint foreground) => ToggleDark(Shown.FirstOrDefault(w => foreground != 0 && w.WindowHandle == foreground));

    /// <summary>Ctrl+E on the frame showing this account's tab: dims or lights its iPhone, if it is one.</summary>
    public bool ToggleDarkOf(string key) => ToggleDark(Shown.FirstOrDefault(w => w.Key == key));

    private static bool ToggleDark(IosGameWindow? entry)
    {
        entry?.Window.SetDark(null);
        return entry is not null;
    }

    private void SetOpen(bool open)
    {
        Changed?.Invoke(this, EventArgs.Empty);

        if (!open)
        {
            // An unplugged iPhone's row goes once its screen is closed: listed afresh.
            _listed = DateTime.MinValue;
        }
    }

    private void SetProblem(string key, string? problem)
    {
        if (_problems.GetValueOrDefault(key) == problem)
        {
            return;
        }

        if (problem is null)
        {
            _problems.Remove(key);
        }
        else
        {
            _problems[key] = problem;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recherche des iPhone impossible : {message}")]
    private partial void LogListFailed(string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "L'iPhone {name} rebranché n'a pas pu être rouvert : {message}")]
    private partial void LogReopenFailed(string name, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DOFUS Touch n'est pas installé sur l'iPhone {name}.")]
    private partial void LogGameMissing(string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Le jeu n'a pas pu être ouvert sur l'iPhone {name}.")]
    private partial void LogGameFailed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La fenêtre de l'iPhone {name} n'a pas pu être fermée.")]
    private partial void LogCloseFailed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Le jeu n'a pas pu être arrêté sur l'iPhone {name}.")]
    private partial void LogGameStopFailed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Les jeux des iPhone n'ont pas pu être arrêtés à la fermeture.")]
    private partial void LogStopAllFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "L'affichage de l'iPhone {name} a échoué, nouvel essai {attempt}.")]
    private partial void LogReopening(string name, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "L'affichage de l'iPhone {name} est abandonné après {attempts} essai(s).")]
    private partial void LogGaveUp(string name, int attempts);
}

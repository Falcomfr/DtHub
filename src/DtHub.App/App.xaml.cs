using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core;
using DtHub.Core.Devices;
using DtHub.Core.Diagnostics;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Updates;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Storage;
using DtHub.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DtHub.App;

/// <summary>
/// Entry point. Only two cases: first launch, we ask which instances
/// to open; afterwards, we open directly the ones that are checked.
/// </summary>
public partial class App : Application, IDisposable
{
    /// <summary>
    /// Marks the running instance. A second launch, whether from the
    /// desktop shortcut or otherwise, must not open a second set of
    /// windows: it wakes the one already running and exits.
    /// </summary>
    private const string InstanceName = @"Local\DtHub.Instance";

    private const string WakeName = @"Local\DtHub.Wake";

    /// <summary>
    /// Watches the shape of the game windows. The correction only
    /// happens once the size is stable: we do not fight an ongoing
    /// gesture.
    /// </summary>
    private readonly DispatcherTimer _shape = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private IHost? _host;
    private ConfiguratorWindow? _configurator;
    private QuestWindow? _quests;
    private AlmanaxWindow? _almanax;
    /// <summary>
    /// What we allow ourselves to save the state when Windows closes
    /// the session. It grants five; we take three, and the shutdown
    /// follows.
    /// </summary>
    private static readonly TimeSpan SessionSaveLimit = TimeSpan.FromSeconds(3);

    /// <summary>
    /// What we allow ourselves to tidy up on shutdown. Closing the
    /// game windows takes most of this time, and the wait is capped
    /// so that a stubborn window does not hold up the application.
    /// </summary>
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(8);

    private bool _quitting;
    private bool _started;
    private Mutex? _instance;
    private EventWaitHandle? _wake;
    private RegisteredWaitHandle? _wakeRegistration;


    /// <summary>
    /// What we do with a fault. It knows neither the host nor the
    /// windows: it asks for them when it needs them, which lets it
    /// serve before the host exists.
    /// </summary>
    private readonly FaultReporting _faults = new(
        () => (Current as App)?._host?.Services,
        () => (Current as App)?._configurator is { IsVisible: true } panel ? panel : null);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The game windows are not WPF windows: the application must
        // not close when the configurator is hidden.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DarkTitleBar.Arm();


        if (!ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        _faults.Arm(this);

        try
        {
            _host = BuildHost();
            AppHost.Initialize(_host.Services);
            await _host.StartAsync().ConfigureAwait(true);

            // A startup trace guarantees that a log file always
            // exists, even when the session goes without incident.
            Log.Information("{Product} {Version} démarre.", ProductInfo.Name, ProductInfo.Version);

            await RunAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The startup safety net catches everything, that is its
            // role: a fault here would leave an application with no
            // window. Everything except out of memory, where opening
            // one more window would not succeed.
            _faults.Show(exception, Strings.Get("StartupFailed"));
            Shutdown(1);
        }
    }

    /// <summary>Chains possible setup, launch, then configurator.</summary>
    private async Task RunAsync()
    {
        var services = _host!.Services;
        var settings = services.GetRequiredService<SettingsService>();
        var launcher = services.GetRequiredService<GameLauncher>();

        // The language is set before the first window: texts are
        // read when views are built, and a window already built
        // would no longer change language.
        //
        // Reading is kept separate from applying it, and that is not
        // done for show: see ApplyLanguage.
        ApplyLanguage(await ChooseLanguageAsync(settings).ConfigureAwait(true));

        // Before everything else, and before the first window: on a
        // fresh machine, ADB and scrcpy used to install themselves in
        // passing, from two calls that needed something else, nineteen
        // megabytes during which nothing appeared on screen. Shows
        // nothing when they are already present.
        await PreparationWindow
            .RunAsync(services.GetRequiredService<ToolPreparation>())
            .ConfigureAwait(true);

        await SweepLeftoversAsync().ConfigureAwait(true);

        await KillOrphansAsync(services).ConfigureAwait(true);

        launcher.IconDirectory = WindowIcons.EnsureDirectory(services.GetRequiredService<IAppPaths>());

        PlaceShortcut(services);

        // The first launch is recognized by an empty device registry,
        // not by a flag in the settings: it is the fact that matters,
        // and it can already be read.
        var firstRun = (await services.GetRequiredService<IDeviceRegistry>()
            .GetKnownAsync().ConfigureAwait(true)).Count == 0;

        // The configurator exists before the launch: it is the one
        // that will show the problems if there are any.
        _configurator = services.GetRequiredService<ConfiguratorWindow>();

        // The shortcuts are only active when one of our windows is in
        // the foreground. Without adding the quest tracker to that,
        // they would die as soon as it gets focus, which is exactly
        // what we do to read a guide.
        SessionEnding += OnSessionEnding;

        launcher.OwnsWindow = OwnsWindow;

        // Without it, the report announces zero accounts open while
        // two are running, and does not know which names to hide.
        services.GetRequiredService<DiagnosticReporter>().Launcher = launcher;
        launcher.ConfiguratorToggleRequested += (_, _) => Dispatcher.Invoke(ToggleConfigurator);
        launcher.QuestsToggleRequested += (_, _) => Dispatcher.Invoke(ToggleQuests);
        launcher.AlmanaxRequested += (_, _) => Dispatcher.Invoke(ShowAlmanax);

        // The toolbar button goes through the same path as the shortcut.
        services.GetRequiredService<ConfiguratorViewModel>().QuestsRequested +=
            (_, _) => Dispatcher.Invoke(ToggleQuests);

        services.GetRequiredService<ConfiguratorViewModel>().AlmanaxRequested +=
            (_, _) => Dispatcher.Invoke(ShowAlmanax);
        launcher.QuitRequested += (_, _) => Dispatcher.Invoke(async () => await RequestQuitAsync().ConfigureAwait(true));
        // Recovering a lost window is requested from scrcpy's read
        // loop, which does not live on the UI thread. It therefore
        // goes back through the dispatcher, like the shortcuts.
        launcher.RecoveryRequested += (_, request) =>
            Dispatcher.Invoke(() => _ = RecoverAsync(launcher, request));

        // The panel was already hidden: hidden is indeed what we want
        // remembered.
        launcher.LastWindowClosed += (_, _) => Dispatcher.Invoke(
            () => OnNothingLeft(rememberConfigurator: false));

        // Hiding the panel while no game window remains amounts to the
        // same thing as closing the last window with the panel hidden:
        // in both cases nothing is left, and the application must not
        // survive invisible. Without this, it stayed alive with
        // nothing on screen, and relaunching it only brought back the
        // panel, without reopening the instances.
        //
        // The panel itself is recorded as shown: hiding it last does
        // not mean we want it hidden, it is simply how what remained
        // gets closed. Only a panel already hidden before the game
        // windows close counts for that choice.
        _configurator.IsVisibleChanged += (_, _) =>
        {
            if (_started && _configurator?.IsVisible == false)
            {
                OnNothingLeft(rememberConfigurator: true);
            }
        };

        // The named startup session, if there is one, decides what
        // opens. Without it we touch nothing, and the application
        // reopens what was open the previous time, as it has always
        // done.
        await ApplyDefaultLaunchProfileAsync(settings).ConfigureAwait(true);

        // The settings are reread after the named session, which just
        // wrote its own choices there.
        var document = await settings.GetAsync().ConfigureAwait(true);

        // Opening the sessions takes several seconds, and the panel
        // used to appear only afterwards: the screen stayed empty,
        // and the application seemed not to start. When it was shown
        // at exit, we already know it will stay shown whatever the
        // launch result, and nothing then forces us to wait. The
        // first launch is left to its usual course: the pairing
        // window must not open on top of a launch in progress.
        var revealedEarly = !firstRun && StartupPresence.ShowBeforeLaunch(document.ConfiguratorVisible);

        if (revealedEarly)
        {
            RevealConfigurator(document);
        }

        var report = await launcher.LaunchEnabledAsync().ConfigureAwait(true);

        _shape.Tick += (_, _) =>
        {
            // Same reason as for the panel's polling: the quality
            // tier changes along the way, and the interval set at
            // startup was not following it.
            if (_shape.Interval != launcher.Quality.WindowWatch)
            {
                _shape.Interval = launcher.Quality.WindowWatch;
            }

            launcher.Watch();
        };

        _shape.Interval = launcher.Quality.WindowWatch;
        _shape.Start();

        if (!revealedEarly)
        {
            RevealConfigurator(document);
        }

        // On the first launch, the panel stays and opens on Devices:
        // that is where there is nothing yet and everything starts.
        // The pairing window comes on top of it, since without a
        // paired phone no other action makes sense.
        if (firstRun)
        {
            _configurator.ShowDevices();
            _configurator.BeginPairing();
        }
        else if (!StartupPresence.ShowConfigurator(document.ConfiguratorVisible, report.Opened))
        {
            // Instantly, with no fade: this cancels a reveal that was
            // only done to force the layout, and nobody must see it
            // happen.
            _configurator.HideNow();
        }

        // The quest tracker comes back as it was left: open or not,
        // and on the quest being read there. Without this it had to
        // be reopened and its quest found again at every launch.
        if (document.QuestsVisible)
        {
            await RestoreQuestsAsync(document.LastQuestUrl, document.LastQuestStep)
                .ConfigureAwait(true);
        }

        // Updates: cleanup first, then what was just installed
        // announces itself, then we check whether something better
        // exists. The check is not awaited: the application must not
        // start at the pace of the network.
        var updates = services.GetRequiredService<UpdateService>();
        var configuratorModel = services.GetRequiredService<ConfiguratorViewModel>();

        updates.Sweep();
        updates.Changed += (_, _) => Dispatcher.Invoke(() => ShowUpdateState(updates, configuratorModel));
        configuratorModel.UpdateNotesRequested += (_, _) => Dispatcher.Invoke(
            () => ShowNotes(
                $"DT Hub {updates.Available?.Version}",
                updates.Ready
                    ? Strings.Get("UpdateReadyNotes")
                    : Strings.Get("UpdateAvailableNotes"),
                ReleaseNotes.Readable(updates.Available?.Notes)));

        if (updates.TakeNotes() is { Length: > 0 } installed)
        {
            ShowNotes(
                $"DT Hub {updates.Running}",
                Strings.Get("UpdateJustInstalled"),
                installed);
        }

        _ = updates.CheckAsync(document.UpdatesAutomatic);

        // Only from here on does hiding the panel count as the
        // user's decision: the startup hiding, meanwhile, follows the
        // settings.
        _started = true;

        if (!report.AnyOpened)
        {
            Log.Warning(
                "Aucune fenêtre ouverte, les réglages restent affichés : {Problems}",
                report.Problems.Count > 0
                    ? string.Join(" ", report.Problems)
                    : "aucune instance à ouvrir.");
        }
        else
        {
            // **A launch that opened something was silent about what
            // it failed to open.** Three windows out of five counted
            // as a success, and the two missing ones were explained
            // nowhere.
            LogLaunchProblems("Ouverture partielle", report);
        }
    }

    /// <summary>
    /// Sets the interface language on all threads.
    ///
    /// The setting overrides Windows when it is filled in; empty, the
    /// system's display language decides, and English when it is not
    /// translated. Number and date formats are not touched: they
    /// follow the country, which is a separate setting.
    /// </summary>
    private static async Task<string> ChooseLanguageAsync(SettingsService settings)
    {
        var document = await settings.GetAsync().ConfigureAwait(true);
        var language = AppLanguage.Choose(document.Language, CultureInfo.CurrentUICulture.Name);

        Log.Information(
            "Langue de l'interface : {Langue} (réglage {Reglage}, Windows {Windows}).",
            language,
            document.Language,
            CultureInfo.InstalledUICulture.Name);

        return language;
    }

    /// <summary>
    /// Sets the language on the thread that will build the windows.
    ///
    /// **This method must not become "async"; that is the entire
    /// point of a fix confirmed by measurement.** A culture set
    /// inside an asynchronous method travels with the execution
    /// context: it reverts to its previous value as soon as the
    /// method returns control to its caller. The language setting was
    /// therefore read, logged, and had no effect, and the application
    /// still spoke Windows' language.
    ///
    /// Recorded, with the setting set to "en", thirty milliseconds
    /// after applying it and before the first window:
    ///
    /// <code>
    /// Langue de l'interface : en (réglage en, Windows fr-FR).
    /// SONDE culture avant fenetre : fr-FR / fr-FR -> COMPTES DOFUS TOUCH
    /// </code>
    ///
    /// Called from an ordinary method, the setting holds: a
    /// synchronous call neither pushes nor restores a context.
    /// </summary>
    private static void ApplyLanguage(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>
    /// Shows the configurator in its place, ready to use.
    ///
    /// The window must be shown once so that its loading happens and
    /// its scaling is known: PlaceAwayFrom measures the window's own
    /// ratio. The opacity avoids flicker when it must ultimately stay
    /// hidden.
    /// </summary>
    private void RevealConfigurator(AppSettingsDocument document)
    {
        // Belt and suspenders: a closed window does not show itself
        // again, and startup must not die because of it.
        if (_configurator is null || !_configurator.IsLoaded && _quitting)
        {
            return;
        }

        // Shown invisibly first, so the window lays itself out and its
        // scaling can be measured, then revealed once it is in place.
        //
        // The opacity is no longer set from here: the panel animates its
        // own, and an animated value outranks a local one, so assigning
        // it would quietly stop having any effect.
        _configurator!.PrepareHidden();
        _configurator.RestorePlacement(document);
        _configurator.PlaceAwayFrom(document.GameAnchor);
        _configurator.Reveal();
    }

    /// <summary>
    /// Places the shortcuts on the executable, wherever it is.
    ///
    /// The application is not installed: it is a file placed wherever
    /// one wants. Without a shortcut, we go look for it where we put
    /// it, and there is nothing to pin. The shortcut is rewritten at
    /// every startup, so moving the file is enough to fix it.
    ///
    /// Nothing is copied or moved: copying itself would leave an
    /// orphaned executable that would never update, and moving itself
    /// would amount to moving someone else's file without asking them.
    ///
    /// Nothing is done from a source tree: the shortcut would target
    /// the publish output, which the development launcher rewrites
    /// every time. That is the rule already written for the update.
    ///
    /// The Start menu is rewritten every time; the desktop follows a
    /// rule of its own, written in <see cref="ShortcutPlacement" />,
    /// so as not to put back a shortcut that was just deleted.
    ///
    /// A shortcut that Windows refuses blocks nothing: the application
    /// starts.
    /// </summary>
    private static void PlaceShortcut(IServiceProvider services)
    {
        var executable = Environment.ProcessPath;

        if (!UpdatePaths.CanReplace(executable, File.Exists))
        {
            return;
        }

        var writer = services.GetRequiredService<IShortcutWriter>();
        var description = "Ouvrir " + ProductInfo.Name;

        var startMenuLink = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ProductInfo.Name + ".lnk");

        var desktopLink = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ProductInfo.Name + ".lnk");

        // Read before writing anything: the Start menu shortcut is
        // precisely what distinguishes a first install from a
        // deliberately empty desktop, and writing it first would
        // erase the difference.
        var placeDesktop = ShortcutPlacement.ShouldWriteDesktop(
            File.Exists(desktopLink),
            File.Exists(startMenuLink));

        if (!writer.Write(startMenuLink, executable!, description))
        {
            Log.Warning("Le raccourci du menu Démarrer n'a pas pu être posé.");
        }

        if (placeDesktop && !writer.Write(desktopLink, executable!, description))
        {
            Log.Warning("Le raccourci du bureau n'a pas pu être posé.");
        }
    }

    /// <summary>
    /// Windows is closing the session: shutdown, restart, sign out.
    ///
    /// This is a deliberate stop like any other, and it must save
    /// what a "Quit" saves. Without this, restarting the machine
    /// brought the windows back to their place from two runs ago, the
    /// one from the last stop via the button, and the window we had
    /// just moved lost its place.
    ///
    /// The save is awaited, not launched as a background task:
    /// Windows only grants a few seconds before closing by force, and
    /// a write launched without being awaited has no chance of
    /// finishing. It is awaited by keeping the message loop running,
    /// otherwise the continuations that come back on the UI thread
    /// would wait for a thread we had blocked ourselves. A timer caps
    /// the wait: a half written state is better than a session that
    /// holds things up.
    /// </summary>
    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        Log.Information("Fin de session Windows : l'état est retenu avant l'arrêt.");

        var frame = new DispatcherFrame();

        var limit = new DispatcherTimer(
            SessionSaveLimit, DispatcherPriority.Send, (_, _) => frame.Continue = false, Dispatcher);

        limit.Start();

        _ = SaveSessionStateAsync(_configurator?.IsVisible == true)
            .ContinueWith(
                _ => frame.Continue = false,
                TaskScheduler.FromCurrentSynchronizationContext());

        Dispatcher.PushFrame(frame);
        limit.Stop();
    }

    /// <summary>
    /// Deliberate exit, via the "Quit" button. This is the only
    /// moment when the session state is saved for the next launch:
    /// where the windows are, which ones were open, and whether the
    /// configurator was shown.
    ///
    /// Closing a game window by hand therefore changes nothing: it
    /// comes back on the next launch. It is the act of quitting that
    /// is authoritative.
    /// </summary>
    internal async Task RequestQuitAsync()
    {
        // Quitting from the panel or via the shortcut: its state at
        // that moment is what counts.
        await SaveSessionStateAsync(_configurator?.IsVisible == true).ConfigureAwait(true);

        Shutdown();
    }

    /// <summary>
    /// Saves where the windows are and whether the configurator was
    /// shown.
    ///
    /// Called by every exit path, not only the "Quit" button: closing
    /// the last game windows by hand also stops the application, and
    /// that path used to forget to save anything at all. The hidden
    /// configurator would then reopen at the next launch, and the
    /// windows would come back to their place from two runs ago.
    /// </summary>
    private async Task SaveSessionStateAsync(bool configuratorVisible)
    {
        var services = _host?.Services;

        if (services is null)
        {
            return;
        }

        var launcher = services.GetRequiredService<GameLauncher>();
        var settings = services.GetRequiredService<SettingsService>();

        try
        {
            // What will reopen at the next launch is not decided
            // here: it follows the launches and the explicit closes,
            // not the state at the moment we quit.
            await launcher.CaptureGeometriesAsync().ConfigureAwait(true);

            await settings.SetConfiguratorVisibleAsync(configuratorVisible).ConfigureAwait(true);

            if (_configurator is not null)
            {
                await _configurator.SavePlacementAsync().ConfigureAwait(true);
            }

            if (_quests is not null)
            {
                await settings
                    .SetQuestsStateAsync(
                        _quests.IsVisible, _quests.LastQuestUrl, _quests.LastQuestStep)
                    .ConfigureAwait(true);

                await services
                    .GetRequiredService<WindowPlacements>()
                    .SaveAsync(_quests, WindowPlacements.Quests)
                    .ConfigureAwait(true);
            }
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "L'état de la session n'a pas pu être enregistré.");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "L'état de la session n'a pas pu être enregistré.");
        }
    }

    /// <summary>
    /// Deletes the extraction folders left by previous versions.
    ///
    /// Awaited, not launched in the background: when nothing opens,
    /// the application stops three seconds after starting, and the
    /// detached task used to be cut off before it had erased
    /// anything. When there is nothing to do, which is the usual
    /// case, this costs only a folder scan.
    /// </summary>
    private static async Task SweepLeftoversAsync()
    {
        var removed = await Task.Run(BundleLeftovers.Sweep).ConfigureAwait(true);

        if (removed > 0)
        {
            Log.Information(
                "{Count} dossier(s) d'extraction laissé(s) par des versions précédentes effacé(s).",
                removed);
        }
    }

    /// <summary>
    /// Closes the mirroring windows left behind by a previous run
    /// that did not end cleanly. Without this, they would stay on
    /// screen and new ones would keep piling up alongside them.
    /// </summary>
    private static async Task KillOrphansAsync(IServiceProvider services)
    {
        try
        {
            // Without downloading anything: scrcpy never installed
            // means scrcpy never launched, so no window left over
            // from a previous run. Simply asking for the path put
            // eleven megabytes on the path of the first startup, for
            // a cleanup that had nothing to clean up.
            if (services.GetRequiredService<IScrcpyLocator>().TryGetInstalledPath() is not { } path)
            {
                return;
            }

            var killed = OrphanProcesses.KillFrom(path);

            if (killed > 0)
            {
                Log.Information("{Count} fenêtre(s) restée(s) d'une exécution précédente fermée(s).", killed);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A cleanup that cannot happen must not prevent startup.
            Log.Warning(exception, "Le ménage des fenêtres restantes a échoué.");
        }
    }

    /// <summary>
    /// No game window and no panel remain: the application stops.
    ///
    /// Nothing visible would otherwise be left, and the shortcuts do
    /// not respond when none of our windows is in the foreground: the
    /// application would be unreachable other than through Task
    /// Manager. Nothing is saved along the way: closing a window by
    /// hand does not change what must reopen at the next launch.
    /// </summary>
    /// <param name="rememberConfigurator">
    /// What must be remembered about the panel's presence at the next
    /// startup. True when it is the panel we just hid last, false
    /// when it was already hidden before the game windows closed.
    /// </param>
    private void OnNothingLeft(bool rememberConfigurator)
    {
        // Two sessions dying together each report themselves as the
        // last one.
        //
        // The guides keep the application alive just like the panel:
        // they are on screen, they receive the shortcuts, and we
        // consult them with the game windows closed. Without them
        // counted, closing the last game window would take down the
        // guide we were reading.
        // **Never before startup is finished.** A session dying
        // during launch, a phone that does not answer, and this rule
        // would close the configurator before it had even appeared.
        // The rest of startup would then call Show on a window
        // already closed, which WPF refuses: the application would
        // die on "Le démarrage a échoué", without a word on screen.
        // Recorded during a real launch, with scrcpy having returned
        // "Server connection failed".
        if (!_started || _quitting || _configurator?.IsVisible == true || _quests?.IsVisible == true)
        {
            return;
        }

        // Also called when the panel is hidden: game windows may
        // still remain then, and the application must continue.
        if (_host?.Services.GetRequiredService<GameLauncher>().ActiveSessions.Count > 0)
        {
            return;
        }

        _quitting = true;

        Log.Information("Plus aucune fenêtre ni panneau : arrêt.");

        // The windows' geometry has just been lost along with them:
        // there is nothing left to record. The rest of the state,
        // though, must be saved, otherwise the hidden configurator
        // would reopen at the next launch.
        _ = SaveSessionStateAsync(rememberConfigurator).ContinueWith(
            _ => Shutdown(),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Claims the single instance slot, or wakes the execution
    /// already running and returns false. Without this, a second
    /// launch would open a second set of game windows on top of the
    /// first.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        _instance = new Mutex(initiallyOwned: true, InstanceName, out var mine);

        if (!mine)
        {
            if (EventWaitHandle.TryOpenExisting(WakeName, out var running))
            {
                using (running)
                {
                    running.Set();
                }
            }

            return false;
        }

        _wake = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, WakeName);

        _wakeRegistration = ThreadPool.RegisterWaitForSingleObject(
            _wake,
            (_, _) => Dispatcher.Invoke(RevealConfigurator),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    /// <summary>
    /// Releases the single instance slot and its wake signal.
    /// </summary>
    public void Dispose()
    {
        _wakeRegistration?.Unregister(null);
        _wake?.Dispose();
        _instance?.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Brings the configurator back to screen, on a second launch.
    /// </summary>
    /// <summary>
    /// Wakes the execution already running, because the application
    /// was relaunched while it was still running.
    ///
    /// It may have nothing left on screen: closing the game windows
    /// by hand does not stop it as long as the panel is shown, and
    /// hiding the panel afterwards leaves it alive and invisible.
    /// Relaunching then used to bring back only the panel without
    /// reopening the instances, which is not what we expect from a
    /// relaunch. They therefore come back, as at startup, and windows
    /// closed from the panel stay closed since they are no longer
    /// part of the startup set.
    /// </summary>
    private void RevealConfigurator()
    {
        if (_configurator is null)
        {
            return;
        }

        _configurator.Reveal();

        _ = ReopenIfNothingIsRunningAsync();
    }

    private async Task ReopenIfNothingIsRunningAsync()
    {
        var services = _host?.Services;

        if (services is null)
        {
            return;
        }

        var launcher = services.GetRequiredService<GameLauncher>();

        if (launcher.ActiveSessions.Count > 0)
        {
            return;
        }

        try
        {
            LogLaunchProblems(
                "La reprise de la session",
                await launcher.LaunchEnabledAsync().ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La reprise de la session a échoué.");
        }
    }

    /// <summary>
    /// Records what a launch reported, whoever asked for it.
    ///
    /// **A launch report is never discarded.** It used to go to the
    /// banner from a button, to a dialog from another, to the log only
    /// when nothing at all had opened, and nowhere from the two paths
    /// nobody watches: the session resumed at startup and the window
    /// reopened after a drop. Silence there is indistinguishable from
    /// success.
    ///
    /// The log is the floor, not the ceiling: the paths where someone
    /// is waiting also say it on screen.
    /// </summary>
    private static void LogLaunchProblems(string what, LaunchReport report)
    {
        if (report.Problems.Count > 0)
        {
            Log.Warning("{What} : {Problems}", what, string.Join(" ", report.Problems));
        }
    }

    /// <summary>True if the window belongs to the application.</summary>
    private bool OwnsWindow(nint handle)
    {
        if (_configurator is not null && handle == _configurator.Handle)
        {
            return true;
        }

        if (_quests is not null && _quests.Handle == handle)
        {
            return true;
        }

        // The linked pages, opened by a click inside a guide. Without
        // them, the shortcuts would die as soon as one of these
        // windows gets focus, which happens precisely while reading.
        //
        // Through a set of handles and not through the window list:
        // this question is asked from the foreground watcher, which
        // does not live on the UI thread.
        return QuestPageWindow.Owns(handle) || AlmanaxWindow.Owns(handle);
    }

    /// <summary>
    /// Shows or hides the quest tracker. The window is built only on
    /// the first call: it carries a browser, which there would be no
    /// point starting up for someone who does not use it.
    /// </summary>
    private void ToggleQuests() => Quests()?.Toggle();

    /// <summary>
    /// Opens the day's Almanax, one window at a time.
    ///
    /// Only one because there is only one Almanax: two windows would
    /// show the same thing, and the second would make us forget the
    /// first. If it is already there, we bring it to the front.
    ///
    /// It, however, does not count when deciding whether something
    /// remains: we do not open DT Hub just to check a calendar, and
    /// the application does not have to survive for it alone.
    /// </summary>
    private void ShowAlmanax()
    {
        if (_almanax is { IsLoaded: true })
        {
            _almanax.Activate();

            return;
        }

        _almanax = _host?.Services.GetRequiredService<AlmanaxWindow>();

        if (_almanax is null)
        {
            return;
        }

        _almanax.Closed += (_, _) => _almanax = null;

        _ = _almanax.ShowAlmanaxAsync();
    }

    /// <summary>
    /// Reopens a lost game window, after the wait the decision has
    /// set.
    ///
    /// The wait is not decoration: reopening within the second would
    /// fail as long as the connection has not come back, and would
    /// burn an attempt for nothing.
    /// </summary>
    private static async Task RecoverAsync(GameLauncher launcher, RecoveryRequest request)
    {
        try
        {
            await Task.Delay(request.Delay).ConfigureAwait(true);

            LogLaunchProblems(
                "La réouverture",
                await launcher.LaunchAsync([request.Instance]).ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The next failure will decide again. Reporting here
            // would add nothing: the notice shown already says the
            // window is expected, and a failed reopen shows itself
            // by the window not coming back.
            Log.Warning(exception, "La réouverture de {Nom} a échoué.", request.Instance.DisplayName);
        }
    }

    /// <summary>
    /// Reopens the quest tracker on what it was showing at the last
    /// stop.
    /// </summary>
    private async Task RestoreQuestsAsync(string? url, int step)
    {
        if (Quests() is not { } quests)
        {
            return;
        }

        await quests.RestoreAsync(url, step).ConfigureAwait(true);
    }

    /// <summary>
    /// The guides, built on first need.
    ///
    /// They count the same way as the panel when deciding whether
    /// anything remains: hiding them while nothing else is left stops
    /// the application, just like hiding the panel.
    /// </summary>
    private QuestWindow? Quests()
    {
        if (_quests is not null)
        {
            return _quests;
        }

        _quests = _host?.Services.GetRequiredService<QuestWindow>();

        if (_quests is null)
        {
            return null;
        }

        // The panel was already hidden, otherwise we would not be
        // about to stop: hidden is indeed what we want remembered.
        _quests.IsVisibleChanged += (_, _) =>
        {
            if (_started && _quests?.IsVisible == false)
            {
                OnNothingLeft(rememberConfigurator: false);
            }
        };

        return _quests;
    }

    /// <summary>
    /// States in one line where the update stands. The banner only
    /// appears when there is something to say.
    /// </summary>
    private static void ShowUpdateState(UpdateService updates, ConfiguratorViewModel model)
    {
        model.UpdateText = updates.Available is not { } release
            ? string.Empty
            : updates.Ready
                ? Strings.Format("UpdateReadyBanner", release.Version)
                : Strings.Format("UpdateAvailableBanner", release.Version);
    }

    /// <summary>
    /// Opens the release note, placed on the panel if it is there.
    /// </summary>
    private void ShowNotes(string heading, string lead, string notes)
    {
        if (notes.Length == 0)
        {
            return;
        }

        var window = new UpdateWindow(heading, lead, notes);

        if (_configurator?.IsVisible == true)
        {
            window.Owner = _configurator;
        }

        window.Show();
    }

    private void ToggleConfigurator()
    {
        if (_configurator is null)
        {
            return;
        }

        _configurator.Toggle();
    }

    /// <summary>
    /// The shutdown, whichever door it comes through.
    ///
    /// Nothing here is awaited the ordinary way. WPF calls this
    /// method from its own shutdown, and cuts the dispatcher as soon
    /// as it returns control: but an "await" returns control to it at
    /// the first piece of work that does not finish synchronously,
    /// and the continuation would then be posted to a dead
    /// dispatcher.
    ///
    /// The message loop therefore keeps running during the wait, as
    /// at the end of a Windows session, and a timer caps it: a half
    /// tidied shutdown is better than an application that refuses to
    /// die.
    ///
    /// We feared that whatever follows the closing of the game
    /// windows, including applying the update, might never run once
    /// there really are windows to close, since the earlier probe had
    /// only measured the case where there were none. Measured since
    /// then, with two accounts open on a real phone: the windows
    /// close in six hundred and twenty six milliseconds and the whole
    /// shutdown takes six hundred and thirty four, against a cap of
    /// eight seconds. The pumped loop does the trick, and both
    /// durations are now logged: the question will not be asked again
    /// blindly.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        var frame = new DispatcherFrame();
        var limit = new DispatcherTimer(
            ShutdownLimit, DispatcherPriority.Send, (_, _) => frame.Continue = false, Dispatcher);

        limit.Start();

        _ = CloseDownAsync().ContinueWith(
            _ => frame.Continue = false,
            TaskScheduler.FromCurrentSynchronizationContext());

        Dispatcher.PushFrame(frame);
        limit.Stop();

        base.OnExit(e);
    }

    private async Task CloseDownAsync()
    {
        // Timed, and not out of curiosity: everything that follows
        // the closing of the game windows, including applying the
        // update, only runs if that closing returns control before
        // the ShutdownLimit cap. The case with open windows had never
        // been measured, for lack of ever having a trace of it.
        var start = System.Diagnostics.Stopwatch.StartNew();

        if (_host is not null)
        {
            try
            {
                // The game windows are closed along with the
                // application: leaving them open without a
                // configurator would make no sense.
                var launcher = _host.Services.GetRequiredService<GameLauncher>();
                var windows = launcher.ActiveSessions.Count;

                await launcher.CloseAllAsync().ConfigureAwait(true);

                Log.Information(
                    "Arrêt : {windows} fenêtre(s) de jeu fermée(s) en {elapsed} ms.",
                    windows,
                    start.ElapsedMilliseconds);

                // The update is applied here and nowhere else:
                // nothing is running anymore, and the executable
                // renaming itself interrupts no one. It will start at
                // the next launch.
                if (_host.Services.GetRequiredService<UpdateService>().Apply())
                {
                    Log.Information("Mise à jour posée, elle démarrera au prochain lancement.");
                }

                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A best effort shutdown: whatever fails here does
                // not prevent leaving, and the application closes
                // anyway.
                Log.Warning(exception, "Arrêt incomplet.");
            }

            _host.Dispose();
        }

        Dispose();

        Log.Information("Arrêt rangé en {elapsed} ms.", start.ElapsedMilliseconds);

        await Log.CloseAndFlushAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Builds the host. The logs go into the data folder, with daily
    /// rotation and a cap on the number of files.
    /// </summary>
    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddDtHub();

        var paths = builder.Services.BuildServiceProvider().GetRequiredService<IAppPaths>();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()

            // The launch identifier, on every line: this is what lets
            // a report take only the current session from a file
            // where all the day's startups are mixed together.
            .Enrich.WithProperty("Session", AppSession.Id)
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "dthub-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 8 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Session}] "
                    + "{SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        return builder.Build();
    }

    /// <summary>
    /// Opens the named startup session, if there is one.
    ///
    /// The log states what was chosen, and that is essential: a
    /// startup that does not open what we expect otherwise leaves no
    /// trace, and we cannot tell apart a badly saved profile from an
    /// account that vanished from the phone.
    /// </summary>
    private static async Task ApplyDefaultLaunchProfileAsync(SettingsService settings)
    {
        var name = await settings.GetDefaultLaunchProfileAsync().ConfigureAwait(true);

        if (name is null)
        {
            Log.Information("Démarrage sans session nommée : on rouvre ce qui était ouvert.");
            return;
        }

        var keys = await settings.ApplyLaunchProfileAsync(name).ConfigureAwait(true);

        if (keys.Count == 0)
        {
            Log.Warning(
                "La session « {Profile} » n'ouvre aucun compte : elle a disparu des réglages, "
                + "ou tous ses comptes ont été retirés du téléphone.",
                name);

            return;
        }

        Log.Information("Session « {Profile} » retenue : {Count} compte(s).", name, keys.Count);
    }
}

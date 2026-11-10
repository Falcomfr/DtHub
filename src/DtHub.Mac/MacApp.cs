using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core;
using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Updates;
using DtHub.Infrastructure.Adb;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Storage;
using DtHub.Infrastructure.Updates;
using DtHub.Mac.Platform;
using DtHub.Mac.Views;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace DtHub.Mac;

/// <summary>
/// The application, the Mac's share of the PC's App
/// (src/DtHub.App/App.xaml.cs): the same startup, the configurator shown
/// the same way, the same quit. Left out, being Windows' alone: the single
/// instance guard (LaunchServices reopens the running copy instead), the
/// tray icon, desktop shortcuts, the sweep of old extraction folders.
/// Updates are the Mac's own (BundleUpdateService), and what a previous
/// run left only from DT Hub's own tools (SweepLeftoversAsync).
/// </summary>
internal sealed class MacApp : Application
{
    private static ServiceProvider? _services;
    private static IClassicDesktopStyleApplicationLifetime? _desktop;
    private static ConfiguratorWindow? _configurator;
    private static QuestWindow? _quests;
    private static AlmanaxWindow? _almanax;
    private static readonly Dictionary<WebTool, WebToolWindow> WebTools = [];

    /// <summary>Only from here on does hiding the panel count as the player's decision.</summary>
    private static bool _started;

    private readonly DispatcherTimer _shape = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>
    /// True once quitting has begun: windows closing then are not the
    /// player's doing, as WPF's Dispatcher.HasShutdownStarted tells the PC.
    /// </summary>
    public static bool IsShuttingDown { get; private set; }

    /// <summary>The PC's AppHost.Services: for the views that open what they need.</summary>
    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Services are built at startup.");

    public override void Initialize()
    {
        AccessibleNames.Install();

        // The name macOS shows in the menu bar's app menu ("Hide", "Quit").
        Name = "DT Hub";

        // An app menu of our own, even empty, replaces Avalonia's default
        // one and its "About Avalonia" item.
        NativeMenu.SetMenu(this, []);

        // Each window's Edit menu, and View menu when it shows a page.
        MacMenus.Install();

        // The PC has one theme, dark; Fluent's dark variant dresses what is
        // not redrawn, in the PC's accent.
        RequestedThemeVariant = ThemeVariant.Dark;
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Avalonia.Media.Color.Parse("#FF3D82BE") };
        Styles.Add(fluent);
        Styles.Add(new StyleInclude(new Uri("avares://DtHub.Mac/"))
        {
            Source = new Uri("avares://DtHub.Mac/Themes/Controls.axaml"),
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CatchStrayFaults();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            // The game windows are not ours: hiding the panel must not end
            // the application.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _services = MacServices.Build(new AppPaths());
            var sessions = _services.GetRequiredService<ScrcpySessionManager>();

            // Quitting from the menu bar or the Dock goes through the same
            // path as the Quit button: the session saved first, the game
            // windows closed, then the real shutdown.
            // Not cancelled: macOS asks the same way when the player logs out
            // or restarts, and a cancelled request stops the logout. The quit
            // work runs here instead, the UI thread kept turning meanwhile.
            desktop.ShutdownRequested += (_, _) =>
            {
                if (IsShuttingDown)
                {
                    return;
                }

                _ = DtHub.Mac.Pc.Modal.Wait(async () =>
                {
                    await Task.WhenAny(QuitWorkAsync(), Task.Delay(QuitBudget)).ConfigureAwait(true);
                    return true;
                });
            };

            // Logout, restart or shutdown: saved and left before macOS asks,
            // since the answer Avalonia gives that request stops the logout.
            if (OperatingSystem.IsMacOS())
            {
                MacNative.dthub_on_power_off(PowerOff);
            }

            // No job object on macOS to take the scrcpy windows down with
            // us: whatever survived the quit is closed here, or it would
            // outlive the app.
            desktop.Exit += (_, _) =>
            {
                IsShuttingDown = true;
                _shape.Stop();
                // Off the UI thread and bounded: the sessions' window calls
                // must not wait for a UI thread that is waiting for them.
                _ = Task.Run(() => sessions.StopAllAsync()).Wait(QuitBudget);

                // The update is placed here and nowhere else, as the PC's
                // OnExit does: every way out comes through, the Quit button
                // as the last game window closed with the panel hidden.
                _ = Task.Run(ApplyUpdateAsync).Wait(QuitBudget);
                _ = Task.Run(() => _services.DisposeAsync().AsTask()).Wait(QuitBudget);
                Log.CloseAndFlush();
            };

            // A click on the Dock icon brings the panel back, as Ctrl+P and
            // the tray do on the PC.
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e.Kind == ActivationKind.Reopen && !IsShuttingDown && _configurator is not null)
                    {
                        // As a second launch wakes the PC's: the panel, and
                        // the session again if nothing is running.
                        _configurator.Reveal();
                        _ = ReopenIfNothingIsRunningAsync();
                    }
                };
            }

            // Linux's stand-in for the Dock click: DT Hub started again (Program.Instance).
            if (Program.Instance is { } instance)
            {
                instance.Woken += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    if (!IsShuttingDown && _configurator is not null)
                    {
                        _configurator.Reveal();
                        _ = ReopenIfNothingIsRunningAsync();
                    }
                });
            }

            _ = StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Chains the language, the launch, then the configurator, as the PC does.</summary>
    private async Task StartAsync()
    {
        var services = Services;

        try
        {
            await RunAsync(services).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The startup safety net catches everything: a fault here would
            // leave an application with no window.
            Log.Fatal(exception, "{Headline}", Strings.Get("StartupFailed"));
            services.GetRequiredService<IDialogService>().ShowWarning(
                $"{Strings.Get("StartupFailed")}{Environment.NewLine}{exception.Message}");
            IsShuttingDown = true;
            _desktop?.Shutdown(1);
        }
    }

    private async Task RunAsync(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsService>();
        var launcher = services.GetRequiredService<GameLauncher>();

        // DT Hub's icon on the game windows, as the PC gives it to scrcpy.
        launcher.IconDirectory = MacWindowIcons.EnsureDirectory(services.GetRequiredService<IAppPaths>());

        // The language is set before the first window: texts are read when
        // views are built. Reading is kept apart from applying, see
        // ApplyLanguage.
        var languageSettings = await settings.GetAsync().ConfigureAwait(true);
        var language = AppLanguage.Choose(languageSettings.Language, CultureInfo.CurrentUICulture.Name);
        ApplyLanguage(language);

        Log.Information("{Product} {Version} démarre.", ProductInfo.Name, ProductInfo.Version);
        Log.Information(
            "Langue de l'interface : {Langue} (réglage {Reglage}, Windows {Windows}).",
            language, languageSettings.Language, CultureInfo.InstalledUICulture.Name);

        // Before the first window, as on the PC: adb and scrcpy are set up
        // with their progress shown. Shows nothing once they are there, nor
        // on Linux ARM, which has no archive to download.
        await PreparationWindow
            .RunAsync(services.GetRequiredService<MacToolPreparation>())
            .ConfigureAwait(true);

        // macOS only: Linux moves no window.
        if (OperatingSystem.IsMacOS())
        {
            // Before any game window: without the Accessibility permission
            // the layout cannot move them.
            AccessibilityCheck.Run(services.GetRequiredService<IDialogService>());

            await SweepLeftoversAsync(services).ConfigureAwait(true);
        }

        // The first launch is recognized by an empty device registry.
        var firstRun = (await services.GetRequiredService<IDeviceRegistry>()
            .GetKnownAsync().ConfigureAwait(true)).Count == 0;

        // The configurator exists before the launch: it is the one that
        // will show the problems if there are any.
        var configurator = new ConfiguratorWindow(
            services.GetRequiredService<ConfiguratorViewModel>(),
            launcher,
            services.GetRequiredService<WindowPlacements>(),
            services.GetRequiredService<AdbDeviceWatch>(),
            services.GetRequiredService<IDialogService>());
        _configurator = configurator;

        if (_desktop is not null)
        {
            _desktop.MainWindow = configurator;
        }

        // Without it, the report announces zero accounts open while two
        // are running, and does not know which names to hide.
        services.GetRequiredService<DiagnosticReporter>().Launcher = launcher;

        // Our own windows count as home for the shortcuts, as on the PC.
        launcher.OwnsWindow = handle => handle != 0 && (handle == configurator.NativeHandle
            || handle == _quests?.Handle || QuestPageWindow.Owns(handle) || AlmanaxWindow.Owns(handle));

        WireLauncher(services, launcher, configurator);

        // The named startup session, if there is one, decides what opens.
        var openNothing = await ApplyDefaultLaunchProfileAsync(settings).ConfigureAwait(true);

        // Reread after the named session, which just wrote its choices.
        var document = await settings.GetAsync().ConfigureAwait(true);

        // When the panel was shown at exit, nothing forces it to wait for
        // the launch, which takes several seconds.
        var revealedEarly = !firstRun && StartupPresence.ShowBeforeLaunch(document.ConfiguratorVisible);

        if (revealedEarly)
        {
            RevealConfigurator(configurator, document);
        }

        var report = openNothing
            ? new LaunchReport(0, [])
            : await launcher.LaunchEnabledAsync().ConfigureAwait(true);

        _shape.Tick += (_, _) =>
        {
            // The quality tier changes along the way: the interval follows.
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
            RevealConfigurator(configurator, document);
        }

        if (firstRun)
        {
            // On the first launch, the panel stays and opens on Devices,
            // and the pairing window comes over it: without a paired phone
            // no other action makes sense.
            configurator.ShowDevices();
            configurator.BeginPairing();
        }
        else if (!StartupPresence.ShowConfigurator(document.ConfiguratorVisible, report.Opened))
        {
            // Instantly: this cancels a reveal only done to lay it out.
            configurator.HideNow();
        }

        // The quest tracker comes back as it was left: open or not, and on
        // the quest being read there.
        if (document.QuestsVisible && Quests(services) is { } quests)
        {
            await quests.RestoreAsync(document.LastQuestUrl, document.LastQuestStep).ConfigureAwait(true);
        }

        // Updates, as on the PC: cleanup first, then what was just
        // installed announces itself, then a check for something better,
        // not awaited: the application must not start at the pace of the
        // network.
        var updates = services.GetRequiredService<BundleUpdateService>();
        updates.Sweep();

        if (updates.TakeNotes() is { Length: > 0 } installed)
        {
            ShowNotes($"DT Hub {updates.Running}", Strings.Get("UpdateJustInstalled"), installed);
        }

        _ = updates.CheckAsync(document.UpdatesAutomatic);

        _started = true;

        if (openNothing)
        {
            // Already logged by the startup choice.
        }
        else if (!report.AnyOpened)
        {
            Log.Warning(
                "Aucune fenêtre ouverte, les réglages restent affichés : {Problems}",
                report.Problems.Count > 0
                    ? string.Join(" ", report.Problems)
                    : LogMessages.Translate("aucune instance à ouvrir."));
        }
        else
        {
            LogLaunchProblems(LogMessages.Translate("Ouverture partielle"), report);
        }
    }

    /// <summary>The launcher's and the view model's requests, wired as on the PC.</summary>
    private static void WireLauncher(IServiceProvider services, GameLauncher launcher, ConfiguratorWindow configurator)
    {
        var model = services.GetRequiredService<ConfiguratorViewModel>();
        var dialogs = services.GetRequiredService<IDialogService>();
        var dispatcher = Dispatcher.UIThread;

        launcher.ConfiguratorToggleRequested += (_, _) => dispatcher.Post(configurator.Toggle);

        launcher.QuestsToggleRequested += (_, _) => dispatcher.Post(() => Quests(services)?.Toggle());
        launcher.AlmanaxRequested += (_, _) => dispatcher.Post(() => ShowAlmanax(services));
        model.QuestsRequested += (_, _) => dispatcher.Post(() => Quests(services)?.Toggle());
        model.AlmanaxRequested += (_, _) => dispatcher.Post(() => ShowAlmanax(services));
        model.WebToolRequested += (_, tool) => dispatcher.Post(() => ShowWebTool(services, tool));

        var updates = services.GetRequiredService<BundleUpdateService>();

        updates.Changed += (_, _) => dispatcher.Post(() => ShowUpdateState(updates, model));
        model.UpdateNotesRequested += (_, _) => dispatcher.Post(() => ShowNotes(
            $"DT Hub {updates.Available?.Version}",
            updates.Ready ? Strings.Get("UpdateReadyNotes") : Strings.Get("UpdateAvailableNotes"),
            ReleaseNotes.Readable(updates.Available?.Notes)));

        launcher.QuitRequested += (_, _) => dispatcher.Post(() => _ = RequestQuitAsync());

        // Recovering a lost window is requested from scrcpy's read loop,
        // off the interface thread.
        launcher.RecoveryRequested += (_, request) => dispatcher.Post(() => _ = RecoverAsync(launcher, request));

        // Why a window went away is said in the panel, hidden while one
        // plays.
        launcher.WindowGone += (_, _) => dispatcher.Post(() =>
        {
            if (!IsShuttingDown)
            {
                configurator.Reveal();
            }
        });

        // The panel was already hidden: hidden is what we want remembered.
        launcher.LastWindowClosed += (_, _) => dispatcher.Post(() => OnNothingLeft(rememberConfigurator: false));

        // Hiding the panel while no game window remains amounts to closing
        // the last window with the panel hidden.
        configurator.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && _started && !configurator.IsVisible)
            {
                OnNothingLeft(rememberConfigurator: true);
            }
        };
    }

    /// <summary>
    /// The guides, built on first need, as on the PC: they carry a browser,
    /// pointless to start for someone who does not use it. Hiding them while
    /// nothing else is left stops the application, like hiding the panel.
    /// </summary>
    private static QuestWindow? Quests(IServiceProvider services)
    {
        if (_quests is not null || IsShuttingDown)
        {
            return _quests;
        }

        // The web view is macOS's WebKit: on Linux the guides open in the
        // browser.
        if (!OperatingSystem.IsMacOS())
        {
            services.GetRequiredService<IDialogService>().OpenUrl(DtHub.Core.Papycha.PapychaSite.Root);
            return null;
        }

        _quests = services.GetRequiredService<QuestWindow>();
        _quests.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && _started && _quests?.IsVisible == false)
            {
                OnNothingLeft(rememberConfigurator: false);
            }
        };

        return _quests;
    }

    /// <summary>A web tool: brought forward when already open, opened otherwise.</summary>
    private static void ShowWebTool(IServiceProvider services, WebTool tool)
    {
        if (!OperatingSystem.IsMacOS())
        {
            services.GetRequiredService<IDialogService>().OpenUrl(tool switch
            {
                WebTool.Map => DofusTouchMap.Address,
                WebTool.Calcofus => Calcofus.Address,
                WebTool.PapychaTracker => DtHub.Core.Papycha.PapychaSite.TrackerUrl,
                WebTool.Lomitall => Lomitall.Address,
                _ => DofusRoom.Address,
            });
            return;
        }

        // Held from the opening, and visible: a window whose opening threw
        // would otherwise keep the tool from ever opening again.
        if (WebTools.TryGetValue(tool, out var open) && open.IsVisible)
        {
            open.Activate();

            return;
        }

        var window = services.GetRequiredService<WebToolWindow>();

        window.Tool = tool;
        window.Closed += (_, _) =>
        {
            if (WebTools.TryGetValue(tool, out var current) && ReferenceEquals(current, window))
            {
                _ = WebTools.Remove(tool);
            }
        };
        WebTools[tool] = window;
        window.Show();
    }

    /// <summary>
    /// The day's Almanax, one window at a time. It does not count when
    /// deciding whether something remains, as on the PC.
    /// </summary>
    private static void ShowAlmanax(IServiceProvider services)
    {
        if (!OperatingSystem.IsMacOS())
        {
            services.GetRequiredService<IDialogService>().OpenUrl(
                DtHub.Core.Almanax.AlmanaxCalendar.UrlFor(Strings.Spoken.TwoLetterISOLanguageName));
            return;
        }

        if (_almanax is { IsVisible: true })
        {
            _almanax.Activate();

            return;
        }

        var window = services.GetRequiredService<AlmanaxWindow>();

        _almanax = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_almanax, window))
            {
                _almanax = null;
            }
        };

        _ = window.ShowAlmanaxAsync();
    }

    /// <summary>
    /// Sets the interface language. Not async on purpose: a culture set
    /// inside an asynchronous method reverts when it returns.
    /// </summary>
    private static void ApplyLanguage(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // And told once and for all, for the sweeps run from timers.
        Strings.Speak(culture);
    }

    /// <summary>
    /// Shows the configurator in its place: shown invisibly first so it
    /// lays itself out and its placement can be restored.
    /// </summary>
    private static void RevealConfigurator(ConfiguratorWindow configurator, AppSettingsDocument document)
    {
        if (IsShuttingDown)
        {
            return;
        }

        configurator.PrepareHidden();
        configurator.RestorePlacement(document);
        configurator.PlaceAwayFrom(document.GameAnchor);
        configurator.Reveal();
    }

    /// <summary>
    /// Quits the application. Whatever the path, the Quit button, the menu
    /// bar, the Dock or the launcher, the state is saved first.
    /// </summary>
    internal static async Task RequestQuitAsync()
    {
        if (IsShuttingDown || _services is null)
        {
            return;
        }

        try
        {
            await QuitWorkAsync().ConfigureAwait(true);
        }
        finally
        {
            _desktop?.Shutdown();
        }
    }

    /// <summary>Kept alive while the native side holds it.</summary>
    private static readonly MacNative.PowerOffCallback PowerOff = OnPowerOff;

    /// <summary>
    /// The PC's OnSessionEnding: the session saved and the games closed
    /// within the quit's budget, then the application ends by itself.
    /// </summary>
    private static void OnPowerOff()
    {
        if (IsShuttingDown)
        {
            return;
        }

        Log.Information("Fermeture de session macOS : DT Hub enregistre et se ferme.");
        _ = DtHub.Mac.Pc.Modal.Wait(async () =>
        {
            await Task.WhenAny(QuitWorkAsync(), Task.Delay(QuitBudget)).ConfigureAwait(true);
            return true;
        });
        _desktop?.Shutdown();
    }

    /// <summary>
    /// The longest a quit waits for the games to close and the state to be
    /// saved: past it, DT Hub ends anyway rather than hang.
    /// </summary>
    private static readonly TimeSpan QuitBudget = TimeSpan.FromSeconds(10);

    /// <summary>What a quit does before the application ends.</summary>
    private static async Task QuitWorkAsync()
    {
        if (IsShuttingDown || _services is null)
        {
            return;
        }

        IsShuttingDown = true;
        _configurator?.BeginQuit();

        // Before the save: a window that ends meanwhile would read as
        // closed by hand, and leave the next startup.
        _ = _services.GetRequiredService<ScrcpySessionManager>().MarkAllStopping();

        await SaveSessionStateAsync(_configurator?.IsVisible == true).ConfigureAwait(true);
        await CloseDownAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Places a prepared update, at exit: nothing is running anymore. The
    /// box is read now: unchecked after the download, the player keeps his
    /// version.
    /// </summary>
    private static async Task ApplyUpdateAsync()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var settings = await _services.GetRequiredService<SettingsService>().GetAsync().ConfigureAwait(false);

            if (_services.GetRequiredService<BundleUpdateService>().Apply(settings.UpdatesAutomatic))
            {
                Log.Information("Mise à jour posée, elle démarrera au prochain lancement.");
            }
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "La mise à jour n'a pas pu être posée.");
        }
    }

    /// <summary>States in one line where the update stands, as the PC's banner.</summary>
    private static void ShowUpdateState(BundleUpdateService updates, ConfiguratorViewModel model) =>
        model.UpdateText = updates.Available is not { } release
            ? string.Empty
            : updates.Ready
                ? Strings.Format("UpdateReadyBanner", release.Version)
                : Strings.Format("UpdateAvailableBanner", release.Version);

    /// <summary>Opens the release note, over the panel when it is shown.</summary>
    private static void ShowNotes(string heading, string lead, string notes)
    {
        if (notes.Length == 0)
        {
            return;
        }

        var window = new UpdateWindow(heading, lead, notes);

        if (_configurator?.IsVisible == true)
        {
            window.Show(_configurator);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>
    /// The PC's fault reporting (FaultReporting.cs) is WPF's: here a fault
    /// that nothing caught is at least written down, and one raised on the
    /// UI thread does not take DT Hub down with the games still open.
    /// </summary>
    private static void CatchStrayFaults()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "{Headline}", Strings.Get("UnexpectedError"));
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Exception non interceptée.");
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Exception de tâche non observée.");
            e.SetObserved();
        };
    }

    /// <summary>Saves where the windows are and whether the configurator was shown.</summary>
    private static async Task SaveSessionStateAsync(bool configuratorVisible)
    {
        if (_services is null)
        {
            return;
        }

        var launcher = _services.GetRequiredService<GameLauncher>();
        var settings = _services.GetRequiredService<SettingsService>();

        try
        {
            await launcher.CaptureGeometriesAsync().ConfigureAwait(true);
            await settings.SetConfiguratorVisibleAsync(configuratorVisible).ConfigureAwait(true);

            if (_configurator is not null)
            {
                await _configurator.SavePlacementAsync().ConfigureAwait(true);
            }

            if (_quests is not null)
            {
                await settings
                    .SetQuestsStateAsync(_quests.IsVisible, _quests.LastQuestUrl, _quests.LastQuestStep)
                    .ConfigureAwait(true);

                await _services
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
    /// Closes the game windows with the application, while the interface
    /// thread is still free: the exit handler can only block.
    /// </summary>
    private static async Task CloseDownAsync()
    {
        if (_services is null)
        {
            return;
        }

        var start = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var launcher = _services.GetRequiredService<GameLauncher>();
            var windows = launcher.ActiveSessions.Count;

            await launcher.CloseAllAsync().ConfigureAwait(true);

            Log.Information(
                "Arrêt : {windows} fenêtre(s) de jeu fermée(s) en {elapsed} ms.",
                windows,
                start.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A best effort shutdown: the application closes anyway.
            Log.Warning(exception, "Arrêt incomplet.");
        }
    }

    /// <summary>
    /// No game window and no panel remain: the application stops, as on
    /// the PC. Never before startup is finished, never while accounts wait
    /// for their phone or their window.
    /// </summary>
    private static void OnNothingLeft(bool rememberConfigurator)
    {
        if (!_started || IsShuttingDown || _configurator?.IsVisible == true || _quests?.IsVisible == true
            || _services is null)
        {
            return;
        }

        var launcher = _services.GetRequiredService<GameLauncher>();

        if (launcher.ActiveSessions.Count > 0 || launcher.AwaitsPhone || launcher.Recovers)
        {
            return;
        }

        IsShuttingDown = true;
        _configurator?.BeginQuit();

        Log.Information("Plus aucune fenêtre ni panneau : arrêt.");

        _ = SaveSessionStateAsync(rememberConfigurator).ContinueWith(
            _ => _desktop?.Shutdown(),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Reopens a window lost on the way, after the delay asked.</summary>
    private static async Task RecoverAsync(GameLauncher launcher, RecoveryRequest request)
    {
        try
        {
            await Task.Delay(request.Delay).ConfigureAwait(true);

            // Closing meanwhile, or the player closed the account: the
            // reopening is called off.
            if (IsShuttingDown || !launcher.IsRecovering(request.Instance.Key))
            {
                return;
            }

            var report = await launcher.LaunchAsync([request.Instance]).ConfigureAwait(true);

            LogLaunchProblems(LogMessages.Translate("La réouverture"), report);

            if (!report.AnyOpened)
            {
                launcher.AwaitPhoneIfGone(request.Instance, report.Problems);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La réouverture de {Nom} a échoué.", request.Instance.DisplayName);
            launcher.AwaitPhoneIfGone(request.Instance, []);
        }
    }

    /// <summary>
    /// The PC's KillOrphansAsync, and on the Mac the adb shells too: what a
    /// run that ended badly left running, scrcpy windows and the commands
    /// of a phone that stopped answering. Only DT Hub's own copies: the
    /// tools are its own on the Mac, so nothing of the player's is touched,
    /// but for the adb server, shared by every adb and restarted.
    /// </summary>
    private static async Task SweepLeftoversAsync(IServiceProvider services)
    {
        try
        {
            // The player's own copy is what the locators hand when DT Hub's
            // could not be downloaded: its windows and its adb server are
            // the player's, and stay untouched.
            var tools = services.GetRequiredService<IAppPaths>().ToolsDirectory;

            if (services.GetRequiredService<IScrcpyLocator>().TryGetInstalledPath() is { } scrcpy
                && IsUnder(scrcpy, tools))
            {
                var windows = OrphanProcesses.KillFrom(scrcpy);

                if (windows > 0)
                {
                    Log.Information("{Count} fenêtre(s) restée(s) d'une exécution précédente fermée(s).", windows);
                }
            }

            var adb = await services.GetRequiredService<IAdbLocator>().GetAdbPathAsync().ConfigureAwait(true);

            if (!IsUnder(adb, tools))
            {
                return;
            }

            var shells = MacOrphans.KillShellsFrom(adb);

            if (shells > 0)
            {
                Log.Information("{Count} commande(s) adb restée(s) d'une exécution précédente arrêtée(s).", shells);
            }

            // macOS's local network protection follows the application
            // that started a process, and the adb server outlives it: left
            // by a DT Hub since closed, it is cut off from the phones on
            // Wi-Fi ("No route to host"), and this run, reusing it, saw
            // every wireless phone as refusing it and asked to pair again.
            // Stopped here, the server the first adb call starts is this
            // run's own. The PC has no such protection and keeps it.
            await services.GetRequiredService<IAdbClient>().StopServerAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A cleanup that cannot happen must not prevent startup.
            Log.Warning(exception, "Le ménage des fenêtres restantes a échoué.");
        }
    }

    private static bool IsUnder(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>
    /// The PC's: brought back with no game open, DT Hub reopens the session,
    /// unless the player chose to open nothing at startup.
    /// </summary>
    private static async Task ReopenIfNothingIsRunningAsync()
    {
        if (_services is null)
        {
            return;
        }

        var launcher = _services.GetRequiredService<GameLauncher>();

        if (launcher.ActiveSessions.Count > 0
            || (await _services.GetRequiredService<SettingsService>().GetAsync().ConfigureAwait(true))
                .OpenNothingAtStartup)
        {
            return;
        }

        try
        {
            LogLaunchProblems(
                LogMessages.Translate("La reprise de la session"),
                await launcher.LaunchEnabledAsync().ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La reprise de la session a échoué.");
        }
    }

    private static void LogLaunchProblems(string what, LaunchReport report)
    {
        if (report.Problems.Count > 0)
        {
            Log.Warning("{What} : {Problems}", what, string.Join(" ", report.Problems));
        }
    }

    /// <summary>
    /// The named startup session, the PC's: "open nothing" wins, then a
    /// default profile writes its choices before the launch.
    /// </summary>
    /// <returns>True when nothing must open.</returns>
    private static async Task<bool> ApplyDefaultLaunchProfileAsync(SettingsService settings)
    {
        if ((await settings.GetAsync().ConfigureAwait(true)).OpenNothingAtStartup)
        {
            Log.Information("Démarrage sans ouvrir de jeu, par choix : les comptes restent à lancer à la main.");
            return true;
        }

        var name = await settings.GetDefaultLaunchProfileAsync().ConfigureAwait(true);

        if (name is null)
        {
            Log.Information("Démarrage sans session nommée : on rouvre ce qui était ouvert.");
            return false;
        }

        var keys = await settings.ApplyLaunchProfileAsync(name).ConfigureAwait(true);

        if (keys.Count == 0)
        {
            Log.Warning(
                "La session « {Profile} » n'ouvre aucun compte : elle a disparu des réglages, "
                + "ou tous ses comptes ont été retirés du téléphone.",
                name);

            return false;
        }

        Log.Information("Session « {Profile} » retenue : {Count} compte(s).", name, keys.Count);
        return false;
    }
}

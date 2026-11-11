# iPhone or iPad as an Account Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** an iPhone or iPad becomes an account in the same list as Android accounts, with every window feature, startup, teams, tabs and the game's launch, in one release.

**Architecture:** a small `IGameWindow` interface in Core, implemented by `ScrcpySession` unchanged and by an iOS window adapter in App, lets every window-only feature of `WindowManagerService` and `GameLauncher` serve both kinds; device paths keep reading scrcpy sessions only. iOS accounts are stored instances with `Platform = Ios`; their lifecycle stays in `IPhoneScreens`, driven by a pure state machine in Core.

**Tech Stack:** .NET 10, WPF, WebView2, xUnit; pymobiledevice3 11.49.5 through `PyMobileDevice.MirrorBootstrap` (Python).

**Spec:** `docs/superpowers/specs/2026-11-11-iphone-as-account-design.md`

## Global Constraints

- Never send an adb or scrcpy command with an iOS UDID; device paths take `ScrcpySession`s, never `IGameWindow`s.
- iOS account key: `UDID|0|ios`; `PackageName = "ios"` fixed; bundle id in `IosBundleId`.
- Core keeps no WPF reference.
- Every user-facing string in `Strings.resx`, `Strings.fr.resx`, `Strings.es.resx`; log strings in the three `LogMessages*.resx`.
- No em dash anywhere, comments in English, test names in French (repo convention).
- Each task ends with: `rtk proxy dotnet build -warnaserror` (0 warnings), `rtk proxy dotnet test` (all green), `rtk proxy dotnet format --verify-no-changes` (exit 0). Read the logs, never the exit code alone.
- Work in worktree `C:\Dev\DTHub-iphonecompte`, branch `feat/iphone-compte`; `git status -sb` before each commit; `git add` by explicit paths.

## Review Focus

1. iPad unplugged while docked in the tabbed frame: the tab stays, shows "waiting", returns in place; no adb call.
2. iOS account opened at startup while its device is not listed: a problem on the row, nothing else opens wrong, no exception.
3. Two iOS devices plus Android accounts: F1-F8 and Ctrl+Tab follow the list order exactly.
4. Rotation while docked: the tab's aspect follows, the window does not resize itself against the frame.
5. Ctrl+E with an iPhone shown in a tab (foreground = the frame): that iPhone is dimmed, no Android screen command for its UDID.

Each line has its test in the owning task (Tasks 1, 4, 5, 7).

---

### Task 1: `IGameWindow` and a window manager that takes it

**Files:**
- Create: `src/DtHub.Core/Windows/IGameWindow.cs`
- Modify: `src/DtHub.Core/Scrcpy/ScrcpySession.cs` (implements it), `src/DtHub.Core/Windows/WindowManagerService.cs` (every public method listed below), `src/DtHub.App/Services/GameLauncher.cs` (call sites that use a returned window)
- Test: `tests/DtHub.Tests/Windows/GameWindowTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IGameWindow
  {
      string Id { get; }            // session id; iOS: "ios:" + udid
      string Key { get; }           // account key
      nint WindowHandle { get; }
      bool IsAlive { get; }
      double SourceAspectRatio { get; }   // 0 when unknown
      string WindowTitle { get; }
      int ProcessId { get; }        // owner of the window; iOS: DT Hub's own pid
  }
  ```
  `ScrcpySession : IGameWindow` with `public string Key => Target.Key;`.
- `WindowManagerService`: `EnforceAspect`, `FullscreenMisfits`, `CaptureLost`, `CaptureGeometries`, `Recolour`, `Retitle`, `StackOnActiveAsync`, `StackTargetAsync`, `StackOnAsync`, `TileAsync`, `GridAsync`, `TrackActiveWindow`, `MoveOnlyAsync`, `ApplyOrderAsync`, `ApplySizeAsync`, `ApplyPercentAsync`, `ArrangeAsync`, `RestoreAsync`, `FocusNext`, `FocusPrevious`, `Focus`, `IsManagedWindowFocused`, `RequestClose`, `ResolveWindowAsync` take `IGameWindow` / `IReadOnlyList<IGameWindow>` and return `IGameWindow?` where they returned a session. `ResolveWindowAsync` writes the found handle back only when the window is a `ScrcpySession`.

- [ ] **Step 1: Write the failing tests** in `GameWindowTests` with a test-local `record FakeGameWindow(string Id, string Key, nint WindowHandle, bool IsAlive, double SourceAspectRatio, string WindowTitle, int ProcessId) : IGameWindow` and `FakeWindowController`:
  - `Une_fenetre_qui_n_est_pas_une_session_est_rangee_comme_les_autres`: `TileAsync([fake1, fake2])` moves both handles (`controller.Moves` holds both).
  - `Une_fenetre_qui_n_est_pas_une_session_recoit_le_focus`: `FocusNext([a, b])` with `a` in front returns `b` and focuses `b.WindowHandle`.
  - `Une_fenetre_qui_n_est_pas_une_session_est_recoloree_et_retitree`: `Recolour` and `Retitle` reach its handle.
  - `Une_fenetre_a_poignee_connue_n_est_pas_cherchee_par_son_processus`: `ResolveWindowAsync(fake)` with a live handle returns it without `FindWindows`.
- [ ] **Step 2: Run** `rtk proxy dotnet test tests/DtHub.Tests --filter "FullyQualifiedName~GameWindowTests"`. Expected: build error, `IGameWindow` missing.
- [ ] **Step 3: Implement** the interface, `ScrcpySession : IGameWindow`, and the signature changes; fix `GameLauncher` call sites that read `.Target` from a returned window with `is ScrcpySession s` or `.Key`.
- [ ] **Step 4: Run** the filter, then the full suite. Expected: all green (existing `WindowManagerServiceTests` unchanged).
- [ ] **Step 5: Commit** `feat(windows): a game window can be something other than a scrcpy session`.

### Task 2: iOS accounts in the settings

**Files:**
- Modify: `src/DtHub.Core/Settings/AppSettingsDocument.cs` (`StoredInstance`), `src/DtHub.Core/Dofus/DofusInstance.cs`, `src/DtHub.Core/Settings/SettingsService.cs`
- Create: `src/DtHub.Core/Dofus/DevicePlatform.cs`
- Test: `tests/DtHub.Tests/Settings/IosAccountTests.cs`

**Interfaces:**
- Produces: `public enum DevicePlatform { Android, Ios }`; `StoredInstance.Platform` and `DofusInstance.Platform` (default `Android`), `StoredInstance.IosBundleId` and `DofusInstance.IosBundleId` (`string?`); `public const string IosPackage = "ios";` on `DofusInstance`; `DofusInstance.IsIos => Platform == DevicePlatform.Ios`.
- Produces: `Task<DofusInstance> SettingsService.EnsureIosAccountAsync(IPhoneDevice device, CancellationToken ct = default)`: creates `{DeviceId = udid, UserId = 0, PackageName = "ios", UserName = device.Name, DeviceName = device.Name, Platform = Ios}` at the end of the order if missing, updates `DeviceName` if it changed, returns the account. `Task SettingsService.SetIosBundleIdAsync(string key, string bundleId, CancellationToken ct = default)`.

- [ ] **Step 1: Failing tests**:
  - `Un_iPhone_devient_un_compte_a_sa_premiere_vue`: key `"00008132-001031940A05801C|0|ios"`, `Platform == Ios`, last in order.
  - `Un_iPhone_deja_connu_garde_son_compte_et_son_nom_choisi`: second call keeps `CustomName` and `Order`.
  - `Le_balayage_des_profils_Android_n_oublie_jamais_un_compte_iOS`: `ForgetMissingProfilesAsync` for any Android device leaves the iOS account.
  - `La_plateforme_et_l_identifiant_du_jeu_survivent_a_la_relecture`: save, reload through `JsonDocumentStore`, same `Platform` and `IosBundleId`; an old file without `platform` reads `Android`.
- [ ] **Step 2: Run** filter `IosAccountTests`. Expected: build error.
- [ ] **Step 3: Implement**; map the two fields wherever `StoredInstance` becomes `DofusInstance` (search `new DofusInstance` in Core).
- [ ] **Step 4: Run** filter then full suite. Expected: green.
- [ ] **Step 5: Commit** `feat(iphone): an iPhone or iPad is stored as an account`.

### Task 3: the game on the device, through the stream's own process

**Files:**
- Modify: `src/DtHub.Infrastructure/IPhone/PyMobileDevice.cs` (`MirrorBootstrap`, `MirrorRequest`)
- Create: `src/DtHub.Infrastructure/IPhone/IosGameControl.cs`, `src/DtHub.Core/IPhone/IosApps.cs`
- Test: `tests/DtHub.Tests/IPhone/IosAppsTests.cs`, `tests/DtHub.Tests/IPhone/PyMobileDeviceTests.cs`

**Interfaces:**
- `MirrorRequest(string python, string udid, int port, bool audio, int controlPort)` sets env `DTHUB_CONTROL_PORT`. The bootstrap patches `ScreenStreamServer.__init__` to keep the instance, and on its first stream starts `asyncio.start_server` on `127.0.0.1:DTHUB_CONTROL_PORT` serving, with `AppServiceService(server._rsd)`:
  - `GET /apps` → JSON `[{ "name": str, "bundle": str }]` from `list_apps`
  - `GET /running?bundle=B` → `{"pid": int|null}` from `list_processes`
  - `POST /launch {"bundle": B}` → `launch_application(B)`
  - `POST /stop {"bundle": B}` → `send_signal_to_process(pid, 9)` when running
  Answers `200` with JSON, `500` with `{"error": str}`.
- Produces: `public static string? IosApps.FindDofus(string appsJson)` (case-insensitive match of `"DOFUS Touch"` on name, `null` if absent); `IosGameControl(int controlPort)` with `Task<string?> FindDofusAsync(CancellationToken)`, `Task<bool> IsRunningAsync(string bundle, CancellationToken)`, `Task LaunchAsync(string bundle, CancellationToken)`, `Task StopAsync(string bundle, CancellationToken)`; failures throw `IPhoneException` with a player message.

- [ ] **Step 1: Failing tests**: `L_identifiant_de_DOFUS_Touch_est_lu_dans_la_liste_des_applis` (sample with Safari and `{"name":"DOFUS Touch","bundle":"com.ankama.dofustouch"}`), `Sans_DOFUS_Touch_l_identifiant_est_nul`, `Le_port_de_commande_est_donne_a_l_outil` (`MirrorRequest(...).Environment["DTHUB_CONTROL_PORT"] == "50123"`), `Le_script_de_demarrage_sert_les_quatre_commandes` (bootstrap text contains `/apps`, `/running`, `/launch`, `/stop`).
- [ ] **Step 2: Run** filter `IosAppsTests|PyMobileDeviceTests`. Expected: FAIL.
- [ ] **Step 3: Implement**; then `py -3 -c "import ast,sys; ast.parse(open(sys.argv[1]).read())"` on the bootstrap written to a temp file must pass (syntax only: no device here).
- [ ] **Step 4: Run** filter then full suite. Expected: green.
- [ ] **Step 5: Commit** `feat(iphone): list, launch and stop the game through the stream's process`.

### Task 4: iOS window lifecycle

**Files:**
- Create: `src/DtHub.Core/IPhone/IosWindowLife.cs`, `src/DtHub.App/Services/IosGameWindow.cs`
- Modify: `src/DtHub.App/Services/IPhoneScreens.cs`, `src/DtHub.App/Windows/IPhoneMirrorWindow.xaml.cs`
- Test: `tests/DtHub.Tests/IPhone/IosWindowLifeTests.cs`

**Interfaces:**
- Produces (Core, pure): `public enum IosWindowState { Opening, Open, Unplugged, Failed, Closed }`; `public enum IosEnd { ByHand, ByUs, GaveUp }`; `public sealed class IosWindowLife` with `State`, `Attempts`, `void Opened()`, `void Unplug()`, `void Replugged()`, `IosEnd? Fail(int maxAttempts)` (returns `GaveUp` past `maxAttempts`, else null and `State = Failed` awaiting reopen), `IosEnd Close(bool byHand)`, `bool CountsPlaytime` (true only in `Open`), `bool KeepsStartup(IosEnd end)` (false only for `ByHand`).
- Produces (App): `sealed class IosGameWindow : IGameWindow` over an `IPhoneMirrorWindow` (`Id = "ios:" + udid`, `Key`, `WindowHandle = window.Handle`, `IsAlive = life.State is Open or Unplugged or Opening`, `SourceAspectRatio = window.Aspect`, `ProcessId = Environment.ProcessId`).
- `IPhoneScreens` becomes keyed by account: `IReadOnlyList<IosGameWindow> Windows`, `Task OpenAsync(DofusInstance account, IPhoneDevice device, CancellationToken ct)`, `Task CloseAsync(string key, bool byHand)`, `IReadOnlyList<IPhoneDevice> Listed`, events `EventHandler<string>? Opened`, `EventHandler<(string Key, IosEnd End)>? Ended`, `EventHandler<string>? AspectChanged`, `EventHandler? Changed` (kept). On open: start stream, then game launch through Task 3 (`IosBundleId` found and stored once). Failure reopens with Android's attempt count (`SessionRecovery`'s constant). `TakeScreensForArranging` is deleted.
- `IPhoneMirrorWindow` gains `event EventHandler? AspectChanged`, `bool Docked { get; set; }` (when true, `FitTo` only raises `AspectChanged`), `void SendBlur()`.

- [ ] **Step 1: Failing tests** on `IosWindowLife`: `Un_debranchement_garde_le_compte_ouvert_et_met_le_temps_en_pause`, `Une_erreur_rouvre_jusqu_au_nombre_d_essais_d_Android_puis_abandonne`, `Une_fermeture_a_la_croix_retire_le_compte_du_demarrage`, `Un_abandon_garde_le_compte_au_demarrage`, `Une_fermeture_par_DT_Hub_garde_le_demarrage`.
- [ ] **Step 2: Run** filter `IosWindowLifeTests`. Expected: build error.
- [ ] **Step 3: Implement** the state machine, then wire `IPhoneScreens` and the window on it.
- [ ] **Step 4: Run** filter then full suite. Expected: green.
- [ ] **Step 5: Commit** `feat(iphone): an iOS window lives like an account's window`.

### Task 5: one list of game windows in the launcher

**Files:**
- Create: `src/DtHub.Core/Windows/GameWindowList.cs`
- Modify: `src/DtHub.App/Services/GameLauncher.cs`
- Test: `tests/DtHub.Tests/Windows/GameWindowListTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlyList<IGameWindow> GameWindowList.Managed(IEnumerable<IGameWindow> windows, IReadOnlySet<string> unmanaged, IReadOnlySet<string> tabbed, Func<string, int> order)` (alive, not locked, not tabbed, by order) and `GameWindowList.Accounts(IEnumerable<IGameWindow> windows, Func<string, int> order)` (alive, by order; transient scrcpy windows excluded by the caller).
- `GameLauncher`: `IReadOnlyList<IGameWindow> ManagedWindows` (scrcpy managed sessions + `_iphones.Windows`) and `AccountWindows()` replace `ManagedSessions`/`AccountSessions()` in: `CycleTabsAsync`, `FocusAccountAsync`, `AccountKeyOf`, `StackOnActiveAsync`, `ArrangeAsync`, `TileAsync`, `GridAsync` (iPhone special case removed), `ApplySizeAsync`, `ToggleFullscreenAsync` and its enter/leave, `RefreshWindowTitlesAsync` (iOS title: key and name, no status), `RefreshWindowColours`, `CaptureGeometriesAsync`, `ApplyWindowOrderAsync`, `Watch`'s `EnforceAspect`, `OnForegroundChanged` (iPhone window = game in front: arms Échap), `ArrangeableCount`. Device paths (`PlayingPhones`, health, sound, screen, game watch, reopen, restart, `Routable`) keep `ActiveSessions`.
- `TogglePanelsAsync`: when the frame is in front and its selected tab is an iOS key, dim that iPhone and return.
- Playtime: `Ended` and unplug/replug drive `CountPlaytime` for iOS keys with `IosWindowLife.CountsPlaytime`.

- [ ] **Step 1: Failing tests**: `Les_fenetres_iOS_et_Android_suivent_l_ordre_de_la_liste` (Review Focus 3: android A order 0, iOS B order 1, android C order 2 → A, B, C), `Une_fenetre_verrouillee_ou_en_onglet_n_est_pas_rangee`, `Une_fenetre_morte_n_a_pas_de_touche`.
- [ ] **Step 2: Run** filter `GameWindowListTests`. Expected: build error.
- [ ] **Step 3: Implement** `GameWindowList`, then switch every launcher method listed above to it.
- [ ] **Step 4: Run** filter, full suite, then `grep -n "_iphones\|IosGameWindow" src/DtHub.App/Services/GameLauncher.cs` and check none sits in a device path listed above (Review Focus 5 for `TogglePanelsAsync`, checked in Task 9's real run).
- [ ] **Step 5: Commit** `feat(iphone): window shortcuts and arranging serve iOS accounts`.

### Task 6: launch, close, startup and teams for iOS accounts

**Files:**
- Modify: `src/DtHub.App/Services/GameLauncher.cs` (`LaunchAsync`, `LaunchEnabledAsync`, `StopSessionAsync`, `CloseAllAsync`, `CloseTabbedAsync`, `RestartAsync`, `StopDeviceAsync`, `ForgetDeviceAsync`, `IsOpen`, `FindSession` callers)
- Create: `src/DtHub.Core/IPhone/IosLaunchSplit.cs`
- Test: `tests/DtHub.Tests/IPhone/IosLaunchSplitTests.cs`

**Interfaces:**
- Produces: `public static (IReadOnlyList<DofusInstance> Android, IReadOnlyList<DofusInstance> Ios) IosLaunchSplit.Split(IEnumerable<DofusInstance> accounts)` and `public static IReadOnlyList<string> IosLaunchSplit.Missing(IEnumerable<DofusInstance> ios, IReadOnlySet<string> listedUdids)`.
- `LaunchAsync`: Android part unchanged; iOS part opens each listed one through `IPhoneScreens.OpenAsync`, adds `Strings.Format("IosNotPlugged", name)` to problems for the missing ones, then `SetInstancesEnabledAsync` for the opened keys. `StopSessionAsync`/`CloseAllAsync`/`CloseTabbedAsync` close iOS keys with `CloseAsync(key, byHand: false)`, stopping the game first when `StopAppOnClose` is on and the device is listed. `RestartAsync` for iOS: stop then launch the game, window kept. `ForgetDeviceAsync` for a UDID removes the iOS account. An opened iOS window gets its remembered place through `WindowManagerService.RestoreAsync` with its `IosGameWindow`, as an Android window does after launch.

- [ ] **Step 1: Failing tests**: `Une_team_melangee_ouvre_les_iOS_a_part`, `Un_iPhone_absent_est_signale_et_ne_bloque_pas_les_autres` (Review Focus 2).
- [ ] **Step 2: Run** filter `IosLaunchSplitTests`. Expected: build error.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** filter then full suite. Expected: green.
- [ ] **Step 5: Commit** `feat(iphone): iOS accounts open at startup, with teams, and close like the others`.

### Task 7: iOS accounts in the tabbed frame

**Files:**
- Modify: `src/DtHub.App/Windows/TabbedGameWindow.xaml.cs`, `src/DtHub.App/ViewModels/GameTabViewModel.cs`, `src/DtHub.App/Services/GameLauncher.cs` (`AttachToTabsAsync` gains an iOS overload, `SetTabbedAsync`, `Watch`'s `KeepOnly` keys), `src/DtHub.App/Windows/IPhoneMirrorWindow.xaml.cs`

**Interfaces:**
- `GameTabViewModel.Aspect` becomes settable; `TabbedGameWindow.UpdateAspect(string key, double aspect)` refits when the tab is shown.
- `KeepOnly` receives scrcpy keys plus `_iphones.Windows` keys (unplugged ones count as waiting).
- On frame deactivation and on tab change, the docked iOS window gets `SendBlur()`.

- [ ] **Step 1: Spike first** (throwaway, `C:\Dev\dthub-uia\ipadclic`): extend `Harness/Program.cs` to create a `TabbedGameWindow` and `Attach` the `IPhoneMirrorWindow` handle with `Dock`; real click (`clic.py`) in the tab must add a `POST /touch` line to `posts.log`; Ctrl+Tab between two harness windows keeps working. If it fails: host the window's `WebView2` directly in the tab (spec, Tabs) and note it in the commit.
- [ ] **Step 2: Implement** docking for iOS accounts with `Docked = true`, aspect pushed through `AspectChanged` → `UpdateAspect` (Review Focus 4), blur on deactivate/tab change, unplug keeps the tab waiting (Review Focus 1).
- [ ] **Step 3: Verify on the harness**: dock, rotate (send `shape:` with swapped size from the fake page), undock, close by the tab's cross; `posts.log` gets a touch in tab and out of it.
- [ ] **Step 4: Run** full suite. Expected: green.
- [ ] **Step 5: Commit** `feat(iphone): iOS accounts live in the tabbed frame`.

### Task 8: the row in the account list

**Files:**
- Modify: `src/DtHub.App/ViewModels/InstanceListViewModel.cs`, `src/DtHub.App/ViewModels/InstanceRowViewModel.cs`, `src/DtHub.App/ViewModels/DeviceGroupViewModel.cs`, `src/DtHub.App/Windows/InstanceListControl.xaml`, `src/DtHub.App/Windows/InstanceListControl.xaml.cs`, the three `Strings*.resx`
- Delete: `src/DtHub.App/ViewModels/IPhoneRowViewModel.cs` and the separate iPhone rows section of `InstanceListControl.xaml`

**Interfaces:**
- `DeviceGroupViewModel.Update(IPhoneDevice device)` (name, system and version, no battery).
- Each sweep: for each `IPhoneScreens.Listed` device, `EnsureIosAccountAsync`; `ShowList` keeps iOS rows whose UDID is listed or whose window is open.
- `InstanceRowViewModel.IsIos`; the row hides quality, distance, phone format, game mode, reduced render and "+" when `IsIos`; status from `IosWindowState` (strings `IosOpening`, `IosOpen`, `IosUnplugged`, `IosFailed`, `IosGameMissing`, `IosNotPlugged`).
- `IPhoneRowViewModel.TooOldHelp` moves to `DeviceGroupViewModel` (header problem).

- [ ] **Step 1: Implement** and remove the old section.
- [ ] **Step 2: Verify the rendering** with the repo's throwaway WPF approach (memory `dthub-rendu-liste-sans-telephone`): an iOS row next to Android rows, both themes; capture `C:\Dev\dthub-uia\ipadclic\liste.png` and look at it.
- [ ] **Step 3: Run** full suite. Expected: green.
- [ ] **Step 4: Commit** `feat(iphone): an iPhone or iPad has an account row like the others`.

### Task 9: real run, records and changelog

**Files:**
- Modify: `docs/DECISIONS.md` (D233), `CHANGELOG.md`, `CHANGELOG.fr.md`, `CHANGELOG.es.md` (`## [Unreleased]` section, same entry count in each)

- [ ] **Step 1: Real run** of the dev build on the owner's PC with the harness server as the "device" where possible, plus the owner's Android phone: Android accounts unchanged (launch, tabs, F-keys, Ctrl+E, close); the iOS window path (harness) docked with Android tabs, Ctrl+Tab across both, Ctrl+E with the iOS tab in front (Review Focus 5), full screen and back. Restore `settings.json` from a copy taken before.
- [ ] **Step 2: Write D233** (what iOS accounts do, what iOS cannot, what a device must still confirm: real touches, bundle id and launch, unplug in a tab) and the changelog entries, including the iPad sound line from `fix/ipad-sans-son`.
- [ ] **Step 3: Run** build, full suite, format. Expected: all clean.
- [ ] **Step 4: Commit** `docs: D233 and changelog for iOS accounts`.

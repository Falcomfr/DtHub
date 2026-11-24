# Samsung game optimizing switch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a Samsung player turn Samsung's game optimizing service (GOS) off and back on from an account's settings popup, and log its state when a Samsung connects.

**Architecture:** A small Core helper reads and changes the GOS state through adb (`pm list packages -e/-d`, `pm disable-user`, `pm enable`). `GameLauncher` reads it once per Samsung connection in the device sweep, keeps it per device, logs it, and applies a change for a row. The row shows the section only for the original game on profile 0 of a phone whose state is known, and the list wires the confirmation, the call and the restart notice. Windows XAML and Mac AXAML both get the section.

**Tech Stack:** .NET (C#), WPF (Windows), Avalonia (Mac/Linux), CommunityToolkit.Mvvm, xUnit with `FakeAdbClient`.

**Spec:** `docs/superpowers/specs/2026-11-24-samsung-game-optimizing-design.md`

## Global Constraints

- GOS package: `com.samsung.android.game.gos`. Game package: `DofusPackages.DofusTouch` (`com.ankama.dofustouch`).
- Only phones whose `AndroidDevice.Manufacturer` equals `samsung`, ignoring case (same test as `DofusInstanceService.IsSecureFolder`).
- Read: `pm list packages -e --user 0 com.samsung.android.game.gos` (on), then `pm list packages -d --user 0 com.samsung.android.game.gos` (off); neither listing exactly that package = absent. `pm list packages` filters by substring: compare exact names.
- Turn off: `pm disable-user --user 0 com.samsung.android.game.gos`. Turn on: `pm enable com.samsung.android.game.gos`. Never trust their output text; read the state again.
- Read once per Samsung connection (device sweep) and after each change. Never polled.
- Confirmation before turning off only. Restart notice after any accepted change.
- Every new file: UTF-8 with BOM (`.editorconfig` `charset = utf-8-bom`); the CI runs `dotnet format DtHub.slnx --verify-no-changes --no-restore`.
- UI strings in `Strings.resx` (en), `Strings.fr.resx`, `Strings.es.resx` with identical keys (`StringsResourceTests` checks it). Log messages: French text in `[LoggerMessage]`, same text as key in `LogMessages.resx`, `.fr.resx`, `.es.resx`.
- .NET commands go through `rtk proxy` with a log file (rtk's dotnet verdict is wrong), then grep the log.
- Test names in French, like the neighbouring tests.

## Review Focus

1. adb fails while reading (phone just unplugged, shell error): the state stays unknown, no section, no "absent" logged, no exception. Test in Task 1.
2. Another package whose name contains the GOS name is listed: not taken for GOS. Test in Task 1.
3. Samsung accepts `pm disable-user` but GOS stays enabled: the player gets the refusal banner, not the restart notice. Test in Task 1 (state read back), handling in Task 2.
4. A renamed copy, a profile other than 0, or an iPhone row on the same Samsung: no section. Test in Task 3.
5. A phone that is not Samsung: no adb call at all. Test in Task 1.

---

### Task 1: Read and change the GOS state (Core)

**Files:**
- Create: `src/DtHub.Core/Android/SamsungGameOptimizing.cs`
- Test: `tests/DtHub.Tests/Android/SamsungGameOptimizingTests.cs`

**Interfaces:**
- Produces:
  - `public enum GameOptimizingState { Absent, On, Off }` (namespace `DtHub.Core.Android`)
  - `public static class SamsungGameOptimizing`
    - `public const string Package = "com.samsung.android.game.gos";`
    - `public static bool Concerns(string? manufacturer)`: true for `samsung`, any case.
    - `public static Task<GameOptimizingState?> ReadAsync(IAdbClient adb, string serial, CancellationToken cancellationToken = default)`: `null` when adb throws `AdbException`.
    - `public static Task<GameOptimizingState?> SetAsync(IAdbClient adb, string serial, bool on, CancellationToken cancellationToken = default)`: runs the change, swallows its `AdbException`, then returns `ReadAsync`.

- [ ] **Step 1: Write the failing tests** (build the fake like `AndroidAppLauncherTests`, serial `"R5CW612EFBT"`)

```csharp
[Fact] public async Task Un_GOS_actif_est_lu_actif()
// WithShell("pm list packages -e", "package:com.samsung.android.game.gos") → On
[Fact] public async Task Un_GOS_coupe_est_lu_coupe()
// WithShell("pm list packages -e", ""), WithShell("pm list packages -d", "package:com.samsung.android.game.gos") → Off
[Fact] public async Task Sans_GOS_l_etat_est_absent()
// both listings "" → Absent
[Fact] public async Task Un_paquet_au_nom_voisin_n_est_pas_GOS()
// -e answers "package:com.samsung.android.game.gos.plugin", -d answers "" → Absent
[Fact] public async Task Une_lecture_qui_echoue_ne_dit_rien()
// FailShell("pm list packages", AdbErrorKind.DeviceOffline) → null
[Fact] public async Task Couper_GOS_envoie_disable_user_puis_relit()
// WithShell("pm disable-user", "Package com.samsung.android.game.gos new state: disabled-user")
// listings answer Off → result Off; Assert.Contains("pm disable-user --user 0 com.samsung.android.game.gos", adb.ShellCalls)
// and the last ShellCalls entry starts with "pm list packages"
[Fact] public async Task Reactiver_GOS_envoie_enable()
// WithShell("pm enable", "..."), listings answer On → On; Assert.Contains("pm enable com.samsung.android.game.gos", adb.ShellCalls)
[Fact] public async Task Un_refus_silencieux_se_voit_a_la_relecture()
// disable-user answers anything, -e still lists GOS → result On
[Theory] [InlineData("samsung", true)] [InlineData("Samsung", true)] [InlineData("Xiaomi", false)] [InlineData(null, false)]
public void Seul_un_Samsung_est_concerne(string? manufacturer, bool expected)
```

- [ ] **Step 2: Run them to verify they fail**

Run: `rtk proxy dotnet test tests/DtHub.Tests --filter FullyQualifiedName~SamsungGameOptimizing > /tmp/gos-t1.log 2>&1; grep -E "error|Passed!|Failed!" /tmp/gos-t1.log | tail -5`
Expected: build error, `SamsungGameOptimizing` does not exist.

- [ ] **Step 3: Implement `SamsungGameOptimizing`** in `src/DtHub.Core/Android/SamsungGameOptimizing.cs`, with `GameOptimizingState` in the same file. Parse listings with `PackageParser.ParsePackageList` and compare with `string.Equals(..., Package, StringComparison.Ordinal)`. Shell arguments as separate list items (`["pm", "list", "packages", "-e", "--user", "0", Package]`), as `AndroidAppLauncher` does. One-line XML doc on the class citing the 2026-11-24 S21 FE finding.

- [ ] **Step 4: Run the tests to verify they pass**

Run: same as Step 2. Expected: `Passed!`, 12 tests (8 facts + 4 theory rows).

- [ ] **Step 5: Commit**

```bash
git add src/DtHub.Core/Android/SamsungGameOptimizing.cs tests/DtHub.Tests/Android/SamsungGameOptimizingTests.cs
git commit -m "feat: read and change Samsung's game optimizing service"
```

### Task 2: Probe at connection, log, apply (GameLauncher)

**Files:**
- Modify: `src/DtHub.App/Services/GameLauncher.cs` (fields near `_encodersAsked` ~950; call in `RefreshDevicesAsync` ~1912 after `RememberSerials`; method next to `SetGameModeAsync` ~4475; log methods next to `LogGameModeSet` ~7308)
- Modify: `src/DtHub.Core/Localization/LogMessages.resx`, `LogMessages.fr.resx`, `LogMessages.es.resx`
- Modify: `src/DtHub.Core/Localization/Strings.resx`, `Strings.fr.resx`, `Strings.es.resx` (key `GameOptimizingRefused`)

**Interfaces:**
- Consumes: Task 1 (`SamsungGameOptimizing.Concerns/ReadAsync/SetAsync`, `GameOptimizingState`).
- Produces:
  - `public GameOptimizingState? GameOptimizingOf(string deviceId)`: `null` when unknown or not a Samsung.
  - `public Task<LaunchReport> SetGameOptimizingAsync(DofusInstance instance, bool on, CancellationToken cancellationToken = default)`: `LaunchReport(0, [])` when the state read back matches `on`, otherwise `LaunchReport(0, [Strings.Format("GameOptimizingRefused", instance.DisplayName)])`; same when no serial is known for the device.

No unit test: `GameLauncher` is only built by DI. Keep it glue; the logic is in Task 1.

- [ ] **Step 1: Add the probe.** Field `ConcurrentDictionary<string, GameOptimizingState?> _gameOptimizing` keyed by device id. New `private void ProbeGameOptimizing(DeviceDiscoveryResult discovery)`, called right after `RememberSerials(discovery)`: for each connected device with a serial and `SamsungGameOptimizing.Concerns(device.Manufacturer)`, `TryAdd(device.Id, null)` and, if added, `Task.Run` a `ReadAsync` that stores the result and calls `LogGameOptimizing(device.Serial, state)` when not null (same shape as `ProbeEncoders`). Remove the entries of devices that are not connected in this discovery, so a reconnection reads and logs again.

- [ ] **Step 2: Add `GameOptimizingOf` and `SetGameOptimizingAsync`.** Serial lookup as in `SetGameModeAsync` (`_serials`, then `FindSession(instance)?.Serial`). Store the state read back, then `LogGameOptimizingSet(instance.DisplayName, on, state)`.

- [ ] **Step 3: Add the log methods and their translations.**

```csharp
[LoggerMessage(Level = LogLevel.Information,
    Message = "Optimisation des jeux Samsung sur {serial} : {state}.")]
private partial void LogGameOptimizing(string serial, GameOptimizingState state);

[LoggerMessage(Level = LogLevel.Information,
    Message = "{Window} : optimisation des jeux Samsung demandée {On}, relue {State}.")]
private partial void LogGameOptimizingSet(string window, bool on, GameOptimizingState? state);
```

Each French text as a `<data name>` in the three `LogMessages*.resx`, values in en / fr / es.

- [ ] **Step 4: Add `GameOptimizingRefused`** to the three `Strings*.resx`. fr: `{0} : le téléphone n'a pas changé l'optimisation des jeux Samsung. À la main : adb shell pm disable-user --user 0 com.samsung.android.game.gos (ou pm enable pour la remettre), puis redémarrez le téléphone.`

- [ ] **Step 5: Build and run the full suite**

Run: `rtk proxy dotnet build DtHub.slnx -warnaserror > /tmp/gos-t2b.log 2>&1; grep -E "error|Warn|avertissement|Build succeeded|Générer a réussi" /tmp/gos-t2b.log | tail -5`
then `rtk proxy dotnet test tests/DtHub.Tests > /tmp/gos-t2t.log 2>&1; grep -E "Passed!|Failed!|error" /tmp/gos-t2t.log | tail -3`
Expected: 0 warning, 0 error; suite green.

- [ ] **Step 6: Commit** (explicit paths: `GameLauncher.cs`, the six resx files)

```bash
git commit -m "feat: probe Samsung's game optimizing service when a Samsung connects"
```

### Task 3: The section in the account's settings

**Files:**
- Modify: `src/DtHub.App/ViewModels/InstanceRowViewModel.cs` (next to the game mode block ~624-655)
- Modify: `src/DtHub.App/ViewModels/InstanceListViewModel.cs` (subscribe next to `GameModeRequested` ~1639; refresh next to `row.GameModes = modes` ~1663; handler next to `OnGameModeRequested` ~1468)
- Modify: `src/DtHub.App/Windows/InstanceListControl.xaml` (after the game mode `StackPanel`, line 931)
- Modify: `src/DtHub.Mac/Views/InstanceListControl.axaml` (after the game mode `StackPanel`, ~640)
- Modify: the three `Strings*.resx`
- Test: `tests/DtHub.Tests/ViewModels/GameOptimizingRowTests.cs`

**Interfaces:**
- Consumes: Task 1 `GameOptimizingState`; Task 2 `GameOptimizingOf`, `SetGameOptimizingAsync`.
- Produces on `InstanceRowViewModel`:
  - `[ObservableProperty] GameOptimizingState? _gameOptimizing` notifying the three booleans below.
  - `public bool OffersGameOptimizingOff`: `GameOptimizing == On` and the row is the original on profile 0 (`!IsIos`, `Instance.UserId == 0`, `Instance.PackageName == DofusPackages.DofusTouch`).
  - `public bool OffersGameOptimizingOn`: same with `Off`.
  - `public bool HasGameOptimizing`: either of the two.
  - `public bool? RequestedGameOptimizing { get; private set; }`, `public event EventHandler<InstanceRowViewModel>? GameOptimizingRequested;`
  - `[RelayCommand] TurnGameOptimizingOff()` / `TurnGameOptimizingOn()`: set `RequestedGameOptimizing` to false / true and raise the event (same shape as `PickGameMode`).

- [ ] **Step 1: Write the failing tests** (build the row like `IosAccountRowTests` line 149, `DofusInstance` with `DeviceId`, `UserId`, `PackageName`)

```csharp
[Fact] public void L_original_du_profil_principal_propose_de_couper_GOS()
// UserId 0, DofusTouch, GameOptimizing = On → OffersGameOptimizingOff, HasGameOptimizing; !OffersGameOptimizingOn
[Fact] public void GOS_coupe_propose_de_le_reactiver()
// same row, Off → OffersGameOptimizingOn; !OffersGameOptimizingOff
[Theory] [InlineData(0, "com.ankama.dofustouch.clone1")] [InlineData(12, "com.ankama.dofustouch")]
public void Une_copie_ou_un_autre_profil_ne_voit_rien(int userId, string package)
// GameOptimizing = On → !HasGameOptimizing
[Fact] public void Un_etat_inconnu_ne_montre_rien()
// GameOptimizing = null or Absent → !HasGameOptimizing
[Fact] public void Le_bouton_demande_la_coupure()
// TurnGameOptimizingOffCommand.Execute(null) → event raised once, RequestedGameOptimizing == false
```

- [ ] **Step 2: Run them to verify they fail**

Run: `rtk proxy dotnet test tests/DtHub.Tests --filter FullyQualifiedName~GameOptimizingRow > /tmp/gos-t3.log 2>&1; grep -E "error|Passed!|Failed!" /tmp/gos-t3.log | tail -5`
Expected: build error, `GameOptimizing` not defined on the row.

- [ ] **Step 3: Implement the row members** listed in Interfaces.

- [ ] **Step 4: Run the tests to verify they pass.** Same command. Expected: `Passed!`, 6 tests.

- [ ] **Step 5: Wire the list.** Subscribe `row.GameOptimizingRequested += OnGameOptimizingRequested` where `GameModeRequested` is subscribed. In the refresh, `row.GameOptimizing = _launcher.GameOptimizingOf(instance.DeviceId)`. Handler `private async void OnGameOptimizingRequested(object? sender, InstanceRowViewModel row)`: when turning off, `_dialogs.Confirm(Strings.Format("GameOptimizingConfirm", row.Instance.DeviceName), Strings.Get("GameOptimizingHeading"))` or return; `await ActOnAsync(row, i => _launcher.SetGameOptimizingAsync(i, on), engageDevice: false)`; refresh `row.GameOptimizing`; when it now equals the requested state, `_dialogs.ShowInformation(Strings.Get("GameOptimizingRestart"), Strings.Get("GameOptimizingHeading"))`. Unsubscribe wherever `GameModeRequested` is unsubscribed.

- [ ] **Step 6: Add the section to both popups**, right after the game mode `StackPanel`: same `Groove` separator, `SectionLabel` heading `{loc:T GameOptimizingHeading}`, tooltip `{loc:T GameOptimizingNote}`, two `QuietButton`s bound to `TurnGameOptimizingOffCommand` (visible on `OffersGameOptimizingOff`) and `TurnGameOptimizingOnCommand` (visible on `OffersGameOptimizingOn`); the panel visible on `HasGameOptimizing`. Mac: `IsVisible` and `Classes="section"` as its game mode block does. A comment citing D237.

- [ ] **Step 7: Add the strings** in the three `Strings*.resx` (fr given; en and es translated in the same tone):

| Key | fr |
|---|---|
| `GameOptimizingHeading` | Optimisation des jeux Samsung |
| `GameOptimizingNote` | Le service d'optimisation des jeux de Samsung dessine DOFUS Touch en résolution réduite sur le profil principal : l'image est floue sur le PC. Vaut pour tout le téléphone. |
| `GameOptimizingTurnOff` | Désactiver (image plus nette) |
| `GameOptimizingTurnOn` | Réactiver |
| `GameOptimizingConfirm` | Désactiver l'optimisation des jeux Samsung sur {0} ?\n\nL'image du jeu devrait redevenir nette. Game Booster peut moins bien fonctionner, et une mise à jour de Samsung peut la réactiver. Il faudra redémarrer le téléphone. |
| `GameOptimizingRestart` | C'est fait. Redémarrez le téléphone pour que ça prenne effet. |

- [ ] **Step 8: Build, full suite, format**

Run the Task 2 build and test commands, then `rtk proxy dotnet format DtHub.slnx --verify-no-changes --no-restore > /tmp/gos-fmt.log 2>&1; tail -5 /tmp/gos-fmt.log`
Expected: 0 warning, suite green, format exits clean.

- [ ] **Step 9: Commit** (explicit paths: the two view models, both views, the three `Strings*.resx`, the test file)

```bash
git commit -m "feat: turn Samsung's game optimizing service off from an account's settings"
```

### Task 4: Decision and release notes

**Files:**
- Modify: `docs/DECISIONS.md` (append `## D237 - ...` after D236, line ~12573, same format)
- Modify: `CHANGELOG.md`, `CHANGELOG.fr.md`, `CHANGELOG.es.md` (new `## [Unreleased]` above `## [0.15.5]`)

- [ ] **Step 1: Write D237**: the finding (S21 FE, original blurry, renamed copy and work profile sharp, game mode standard without effect, GOS disabled = sharp), the choice (button on the original of profile 0 only, state read at connection, confirmation, restart notice), what was left out (auto-disable, other brands, render detection), and what is not verified on a phone (every One UI accepting `disable-user`, the need to restart).
- [ ] **Step 2: Add one `[Unreleased]` line per changelog**, in the tone of the 0.15.5 entries.
- [ ] **Step 3: Commit**

```bash
git add docs/DECISIONS.md CHANGELOG.md CHANGELOG.fr.md CHANGELOG.es.md
git commit -m "docs: D237, Samsung's game optimizing service"
```

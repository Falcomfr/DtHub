# MakeInTouch apps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A MakeInTouch block at the bottom of the Devices tab offers to install DTStuff and OcrApp through their Play Store page in a DT Hub window, and opens them, once installed, from one compact line each on the device the player picks.

**Architecture:** A static catalogue in Core says, for each app, which connected devices have it; one setting hides the block; the store page and the app are opened as transient scrcpy sessions (launched, never remembered), the store one carrying a `market://` URI instead of a component; the Devices tab gains a Help menu and the block, bound to the configurator's view model.

**Tech Stack:** C# 13, .NET 10, WPF, xUnit, adb and scrcpy 4.1.

**Spec:** `docs/superpowers/specs/2026-10-10-makeintouch-apps-design.md`

## Global Constraints

- Packages, exactly: `com.makeintouch.dtstuff` (DTStuff), `com.makeintouch.ocrapp` (OcrApp). Names are not translated.
- Store URI: `market://details?id=<package>`. Intent: `am start --user 0 -a android.intent.action.VIEW -d <uri>`, plus `--display <id> --activity-exclude-from-recents` on a virtual display.
- Missing store: `am start` output containing `unable to resolve Intent`.
- Only user 0 counts, on connected devices.
- Every new text exists in `Strings.resx`, `Strings.fr.resx`, `Strings.es.resx`.
- New files: UTF-8 with BOM, per `.editorconfig`. Every silent `catch` carries a comment (`CatchDisciplineTests`).
- CI gates: `dotnet build DtHub.slnx -c Release -warnaserror`, `dotnet test DtHub.slnx -c Release --no-build`, `dotnet format DtHub.slnx --verify-no-changes`.
- Spike done on 2026-10-10, 13T Pro: the intent on virtual display 62 opened `com.android.vending` fullscreen and visible, and a mouse click on Install started the install. Route A stands.

## Review Focus

1. A transient window (store or app) must not come back at the next launch, nor appear as an account, nor write geometry, tabs or play time (Task 3, every settings write on the launch and close paths).
2. A second click on Install or Open while that window is open brings it forward instead of opening another (Task 3).
3. A clone profile (user 10) with DTStuff installed does not count as having it (Task 1 test).
4. The device chosen in the menu goes away before the click lands: the action reports a failure in the banner instead of throwing (Task 3).
5. Quitting with a transient window open closes it like the others and does not trip the "nothing left" rule fixed today (Task 3, manual check in Task 5).

---

### Task 1: Catalogue and per-device availability

**Files:**
- Create: `src/DtHub.Core/Dofus/PartnerApps.cs`
- Test: `tests/DtHub.Tests/Dofus/PartnerAppsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record PartnerApp(string PackageName, string Name, string LineKey, string IconAsset)`; `LineKey` is `PartnerDtStuffLine` / `PartnerOcrAppLine`; `IconAsset` is `/assets/dtstuff.png` / `/assets/ocrapp.png`.
  - `public static class PartnerApps`: `IReadOnlyList<PartnerApp> All`, `bool IsPartner(string packageName)`, `string StoreUri(string packageName)`.
  - `public sealed record PartnerAvailability(PartnerApp App, IReadOnlyList<string> WithApp, IReadOnlyList<string> WithoutApp)` (device ids, in the order given).
  - `IReadOnlyList<PartnerAvailability> PartnerApps.Availability(IReadOnlyList<(string DeviceId, IReadOnlyList<string> PackagesUser0)> devices)`: one entry per app, in `All` order.

- [ ] **Step 1: Write the failing tests**: `Sans_appareil_aucune_app_n_est_disponible` (both entries, empty lists), `Repartit_les_appareils_selon_qu_ils_ont_l_app`, `L_ordre_des_appareils_est_garde`, `Ignore_les_autres_paquets`, `L_adresse_du_store_vise_le_paquet` (`market://details?id=com.makeintouch.ocrapp`). Clones are excluded by the caller passing user 0 only; the test name `Ne_compte_que_les_paquets_fournis` documents it.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~PartnerAppsTests`, expect a compile failure on `PartnerApps`.
- [ ] **Step 3: Implement `PartnerApps`.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `Know the MakeInTouch apps and which phones have them`

### Task 2: The hidden block setting, and the sweep

**Files:**
- Modify: `src/DtHub.Core/Settings/AppSettingsDocument.cs` (next to `ShownApps`, line 38)
- Modify: `src/DtHub.Core/Settings/SettingsService.cs`
- Modify: `src/DtHub.Core/Dofus/DofusInstanceService.cs:593`
- Modify: `src/DtHub.App/Services/GameLauncher.cs:750` (`RefreshInstancesAsync`)
- Test: the existing SettingsService tests in `tests/DtHub.Tests/Settings/`

**Interfaces:**
- Consumes: `PartnerApps.Availability`.
- Produces:
  - `AppSettingsDocument.PartnerAppsCardHidden` (bool, default false); `Task SettingsService.SetPartnerAppsCardHiddenAsync(bool hidden, CancellationToken)`.
  - `public Task<IReadOnlyList<string>?> DofusInstanceService.TryListPackagesAsync(string serial, int userId, CancellationToken)`: today's private `TryEnumerateAsync`, public under that name.
  - `IReadOnlyList<PartnerAvailability> GameLauncher.PartnerApps` (last sweep) and `string GameLauncher.DeviceName(string deviceId)` if no such lookup exists yet.

- [ ] **Step 1: Failing test** `Retient_le_bloc_MakeInTouch_masque`, then implement the setting.
- [ ] **Step 2:** In `RefreshInstancesAsync`, after `_devices.RefreshAsync`: for each connected device, `TryListPackagesAsync(serial, 0)` (null means the device is left out of this sweep's availability), then `PartnerApps = PartnerApps.Availability(...)`. One `pm list packages` per device and sweep.
- [ ] **Step 3:** Build, full suite. `GameLauncher` has no unit test (App project); covered by Task 5.
- [ ] **Step 4: Commit** — `Look for the MakeInTouch apps on every sweep`

### Task 3: Transient windows: store page and app

**Files:**
- Modify: `src/DtHub.Core/Dofus/DofusInstance.cs` (`public string? StartUri { get; init; }`, `public bool IsTransient { get; init; }`)
- Modify: `src/DtHub.Core/Sessions/AndroidAppLauncher.cs` (around `TryStartAsync`, line 160)
- Modify: `src/DtHub.Core/Scrcpy/ScrcpySessionManager.cs:1057` (`LaunchGameAsync`)
- Modify: `src/DtHub.App/Services/GameLauncher.cs`
- Test: the existing AndroidAppLauncher tests in `tests/DtHub.Tests/Sessions/`

**Interfaces:**
- Consumes: `PartnerApps.StoreUri`, `PartnerApp.Name`.
- Produces:
  - `public static IReadOnlyList<string> AndroidAppLauncher.UriArguments(int userId, string uri, int? displayId)`.
  - `public Task<AppLaunchResult> AndroidAppLauncher.OpenUriAsync(string serial, int userId, string uri, int? displayId, CancellationToken)`; failure carrying `Strings.Get("PlayStoreMissing")` when the output contains `unable to resolve Intent`.
  - `public enum PartnerOpening { InWindow, OnPhone, StoreMissing, Failed }`.
  - `public Task<PartnerOpening> GameLauncher.OpenStoreAsync(string deviceId, string packageName, CancellationToken)` and `public Task<PartnerOpening> GameLauncher.OpenPartnerAppAsync(string deviceId, string packageName, CancellationToken)`.

- [ ] **Step 1: Failing tests**: `Les_arguments_ouvrent_l_adresse_sur_l_ecran_virtuel` (expects `["am","start","--user","0","--display","62","--activity-exclude-from-recents","-a","android.intent.action.VIEW","-d","market://details?id=com.makeintouch.dtstuff"]`), `Sans_ecran_virtuel_pas_de_display`, `Un_intent_sans_destinataire_dit_que_le_store_manque` (fake adb output `Error: Activity not started, unable to resolve Intent { act=android.intent.action.VIEW ... }`).
- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement** `UriArguments` and `OpenUriAsync`. In `LaunchGameAsync`, when `session.Target.StartUri` is set: no `ForceStopAsync`, `OpenUriAsync` instead of `LaunchAsync`.
- [ ] **Step 4: Implement the two `GameLauncher` methods.** Store: transient `DofusInstance` with `PackageName = "com.android.vending"`, `UserId = 0`, `AppLabel = "Play Store - " + Name`, `StartUri = StoreUri(package)`, `IsTransient = true`. App: `PackageName = package`, `AppLabel = Name`, `LaunchComponent` from `ResolveComponentAsync`, `IsTransient = true`. Both through the existing private `LaunchAsync(instances, remembered: null)`. Then go through every settings write on the launch and close paths (enabled set, geometry capture, tabs, playtime, recovery) and skip it for `IsTransient` (Review Focus 1). A live session with the same key: bring its window forward, return `InWindow` (Focus 2). Store launch that opened nothing: `OpenUriAsync(serial, 0, uri, null)`, return `OnPhone` or `StoreMissing`. Device gone: `Failed` (Focus 4).
- [ ] **Step 5: Full suite, expect PASS. Commit** — `Open the Play Store page and the MakeInTouch apps in passing windows`

### Task 4: Help menu and the MakeInTouch block

**Files:**
- Create: `assets/dtstuff.png`, `assets/ocrapp.png` (each store page's `og:image`, with `=s128` in place of `=s0-br30`)
- Modify: `src/DtHub.App/DtHub.App.csproj` (two `<Resource Include="..\..\assets\…" Link="assets\…" />`, as `papycha.png`)
- Modify: `THIRD-PARTY-NOTICES.md` (MakeInTouch's agreement, 2026-10-10, both icons)
- Modify: `src/DtHub.App/Windows/ConfiguratorWindow.xaml:953-981`, `ConfiguratorWindow.xaml.cs`, `src/DtHub.App/ViewModels/ConfiguratorViewModel.cs`
- Modify: the three `Strings*.resx`

**Interfaces:**
- Consumes: `GameLauncher.PartnerApps`, `OpenStoreAsync`, `OpenPartnerAppAsync`, `SetPartnerAppsCardHiddenAsync`.
- Produces: `ConfiguratorViewModel.PartnerTiles` (observable list of `PartnerTileViewModel`: `Name`, `Line`, `Icon`, `IsInstalled`, `Devices` for the menu, `ActCommand`), `PartnerBlockHidden`, `HidePartnerBlockCommand`, `ShowPartnerBlockCommand`, refreshed after each instance sweep.

- [ ] **Step 1: Strings**, French copy (English and Spanish to match): `DevicesHelp` « Aide »; `PartnerBlockHeading` « Les apps de MakeInTouch pour DOFUS Touch »; `PartnerDtStuffLine` « Optimisez vos équipements et créez les meilleurs stuffs. »; `PartnerOcrAppLine` « Partez à la conquête du Dofus Ocre. »; `PartnerInstall` « Installer »; `PartnerOpen` « Ouvrir »; `PartnerBlockHide` « Masquer »; `PartnerBlockShow` « Afficher les apps de MakeInTouch »; `PartnerNoDevice` « Connectez d'abord un appareil »; `PartnerOnPhone` « Déverrouillez votre téléphone et appuyez sur Installer. »; `PlayStoreMissing` « Le Play Store est absent de cet appareil. »; `PartnerFailed` « Impossible d'ouvrir {0} sur cet appareil. »
- [ ] **Step 2: XAML.** The two quiet buttons go into a `ContextMenu` of one `Help` button, same visibility binding. Below, the block (`Card` style, accent tint), one entry per app: compact line with Open when installed, presentation with Install otherwise; close cross top right; the 16 px show-again button when hidden. Several target devices: the button opens a `ContextMenu` of their names. Outcomes other than `InWindow` go to the panel's banner.
- [ ] **Step 3:** Build, full suite, render with the throwaway WPF program outside the repository: light and dark, French, both presentations, one installed and one not, hidden. Show the user.
- [ ] **Step 4: Commit** — `Present the MakeInTouch apps under a Help menu`

### Task 5: On a phone, then the record

**Files:**
- Modify: `docs/DECISIONS.md` (D181, in French as the file is)

- [ ] **Step 1: Manual check on the 13T Pro, with the user:** the block shows; Install opens the store window; once DTStuff is installed, its line turns into Open at the next sweep; Open opens DTStuff in a window; quit and relaunch: no store or DTStuff window reopens, no new account row; the panel's state survives the quit.
- [ ] **Step 2:** D181, then commit — `Record the MakeInTouch block`

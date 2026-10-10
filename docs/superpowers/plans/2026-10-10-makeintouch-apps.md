# MakeInTouch apps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Present DTStuff and OcrApp at the bottom of the Devices tab, open their Play Store page in a DT Hub window to install them, and once installed list them as compact rows.

**Architecture:** A static catalogue in Core decides which apps to offer and which to add; two settings record the hidden card and the declined rows; the store page is an ordinary scrcpy session whose target carries a `market://` URI instead of a component; the Devices tab gains a Help menu and a card bound to the configurator's view model.

**Tech Stack:** C# 13, .NET 10, WPF, xUnit, adb and scrcpy 4.1.

**Spec:** `docs/superpowers/specs/2026-10-10-makeintouch-apps-design.md`

## Global Constraints

- Packages, exactly: `com.makeintouch.dtstuff` (DTStuff), `com.makeintouch.ocrapp` (OcrApp). Names are not translated.
- Store URI: `market://details?id=<package>`. Intent: `am start --user 0 -a android.intent.action.VIEW -d <uri>`, plus `--display <id> --activity-exclude-from-recents` on a virtual display.
- Missing store: `am start` output containing `unable to resolve Intent`.
- Auto-add concerns user 0 only.
- Every new text exists in `Strings.resx`, `Strings.fr.resx`, `Strings.es.resx`.
- New files: UTF-8 with BOM, per `.editorconfig`. Every `catch` that stays silent carries a comment (`CatchDisciplineTests`).
- CI gates: `dotnet build DtHub.slnx -c Release -warnaserror`, `dotnet test DtHub.slnx -c Release --no-build`, `dotnet format DtHub.slnx --verify-no-changes`.
- Spike done on 2026-10-10, 13T Pro: the intent on display 62 opened `com.android.vending` fullscreen and visible on the virtual display. Route A stands.

## Review Focus

1. The store window must not come back at the next launch, nor appear as an account: it is launched, never remembered (Task 4 test on the launch path's persistence).
2. A second click on Install while the store window of that device is open brings that window forward instead of opening another (Task 4).
3. An app installed on an offline device only: it is remembered in `ShownApps`, so the card does not offer it (Task 1 test).
4. The player removes a compact row, then reinstalls nothing: the card does not offer the app again while it is still installed on that device (Task 1 test: installed counts, declined or not).
5. A clone profile (user 10) with DTStuff installed adds no row (Task 1 test).

---

### Task 1: Catalogue and the two selections

**Files:**
- Create: `src/DtHub.Core/Dofus/PartnerApps.cs`
- Test: `tests/DtHub.Tests/Dofus/PartnerAppsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record PartnerApp(string PackageName, string Name, string DescriptionKey, string IconAsset)`; `IconAsset` is the pack path, `/assets/dtstuff.png` and `/assets/ocrapp.png`; `DescriptionKey` is `PartnerDtStuffLine` and `PartnerOcrAppLine`.
  - `public static class PartnerApps` with `IReadOnlyList<PartnerApp> All`, `bool IsPartner(string packageName)`, `string StoreUri(string packageName)`.
  - `IReadOnlyList<PartnerApp> Offered(IReadOnlySet<string> installedAnywhere, IReadOnlyList<ShownApp> remembered)`: the apps whose package is in neither.
  - `IReadOnlyList<ShownApp> ToAdd(string deviceId, IReadOnlyList<string> installedForUser0, IReadOnlyList<ShownApp> remembered, IReadOnlySet<string> declinedKeys)`: one `ShownApp(deviceId, 0, package, Name)` per partner installed there, not remembered, not declined.

- [ ] **Step 1: Write the failing tests**

`Propose_les_deux_apps_quand_aucun_appareil_ne_les_a`, `Ne_propose_plus_une_app_installee_sur_un_appareil` (installedAnywhere = {dtstuff} gives only OcrApp), `Ne_propose_pas_une_app_retenue_d_un_appareil_hors_ligne` (remembered holds OcrApp, installedAnywhere empty, gives only DTStuff), `Ajoute_une_app_installee_sur_le_profil_principal`, `N_ajoute_pas_une_app_refusee` (declinedKeys holds `dev|0|com.makeintouch.dtstuff`), `N_ajoute_pas_une_app_deja_retenue`, `Ignore_les_autres_paquets`, `L_adresse_du_store_vise_le_paquet` (`market://details?id=com.makeintouch.ocrapp`).

- [ ] **Step 2: Run, expect a compile failure**

Run: `dotnet test --filter FullyQualifiedName~PartnerAppsTests` — expected: CS0103/CS0246 on `PartnerApps`.

- [ ] **Step 3: Implement `PartnerApps`**

- [ ] **Step 4: Run the tests, expect PASS**

- [ ] **Step 5: Commit** — `Know the MakeInTouch apps and which to offer`

### Task 2: Two settings, auto-add and decline

**Files:**
- Modify: `src/DtHub.Core/Settings/AppSettingsDocument.cs` (next to `ShownApps`, line 38)
- Modify: `src/DtHub.Core/Settings/SettingsService.cs` (next to `SetShownAppsAsync`, line 905)
- Modify: `src/DtHub.App/ViewModels/InstanceListViewModel.cs:1080` (`RemoveAppAsync`)
- Test: the existing SettingsService test file for shown apps (find it with `ct refs SetShownAppsAsync` or grep in `tests/DtHub.Tests/Settings/`)

**Interfaces:**
- Consumes: `PartnerApps.ToAdd`, `PartnerApps.IsPartner`.
- Produces:
  - `AppSettingsDocument.PartnerAppsCardHidden` (bool, default false) and `AppSettingsDocument.DeclinedPartnerApps` (`List<string>` of keys).
  - `Task<bool> SettingsService.AddPartnerAppsAsync(string deviceId, IReadOnlyList<string> installedForUser0, CancellationToken)`: true when it added something.
  - `Task SettingsService.DeclinePartnerAppAsync(string key, CancellationToken)`.
  - `Task SettingsService.SetPartnerAppsCardHiddenAsync(bool hidden, CancellationToken)`.
  - `SetShownAppsAsync` removes from `DeclinedPartnerApps` every key it now holds (ticking in the picker clears a decline).

- [ ] **Step 1: Write the failing tests**: `Ajoute_DTStuff_installee`, `Ne_rajoute_pas_une_app_refusee`, `Cocher_une_app_refusee_la_libere`, `Retient_l_encart_cache`.
- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement**, then in `RemoveAppAsync` call `DeclinePartnerAppAsync(key)` when `PartnerApps.IsPartner(row.Instance.PackageName)`, before `SetShownAppsAsync` (through `GameLauncher`, which already wraps the settings calls `GetShownAppsAsync` and `SetShownAppsAsync`: add `DeclinePartnerAppAsync` beside them).
- [ ] **Step 4: Run the full suite, expect PASS.**
- [ ] **Step 5: Commit** — `Add the MakeInTouch apps to the phones that have them`

### Task 3: Find them during the device sweep

**Files:**
- Modify: `src/DtHub.Core/Dofus/DofusInstanceService.cs:593` (make the full package list callable)
- Modify: `src/DtHub.App/Services/GameLauncher.cs:750` (`RefreshInstancesAsync`)

**Interfaces:**
- Produces:
  - `public Task<IReadOnlyList<string>?> DofusInstanceService.TryListPackagesAsync(string serial, int userId, CancellationToken)`: today's private `TryEnumerateAsync`, made public under that name.
  - `IReadOnlySet<string> GameLauncher.PartnerAppsInstalled`: the partner packages seen on at least one connected device at the last sweep.

- [ ] **Step 1:** In `RefreshInstancesAsync`, after `_devices.RefreshAsync` and before reading `GetShownAppsAsync`, for each connected device: `TryListPackagesAsync(serial, 0)`; null means skip that device; keep the partner packages; call `AddPartnerAppsAsync`. Then read `GetShownAppsAsync`, so the new rows are discovered in the same sweep. One `pm list packages` per device and sweep: acceptable, the sweep already runs several.
- [ ] **Step 2:** Build, full suite. No unit test: `GameLauncher` lives in the App project, which the test project does not reference. Covered by Task 6's manual check.
- [ ] **Step 3: Commit** — `Look for the MakeInTouch apps on every sweep`

### Task 4: Open a store page in a window

**Files:**
- Modify: `src/DtHub.Core/Dofus/DofusInstance.cs` (new `string? StartUri { get; init; }`)
- Modify: `src/DtHub.Core/Sessions/AndroidAppLauncher.cs` (around `TryStartAsync`, line 160)
- Modify: `src/DtHub.Core/Scrcpy/ScrcpySessionManager.cs:1057` (`LaunchGameAsync`)
- Modify: `src/DtHub.App/Services/GameLauncher.cs`
- Test: `tests/DtHub.Tests/Sessions/` (the existing AndroidAppLauncher test file)

**Interfaces:**
- Consumes: `PartnerApps.StoreUri`.
- Produces:
  - `public static IReadOnlyList<string> AndroidAppLauncher.UriArguments(int userId, string uri, int? displayId)`.
  - `public Task<AppLaunchResult> AndroidAppLauncher.OpenUriAsync(string serial, int userId, string uri, int? displayId, CancellationToken)`; failure with `Strings.Get("PlayStoreMissing")` when the output contains `unable to resolve Intent`.
  - `public Task<StoreOpening> GameLauncher.OpenStoreAsync(string deviceId, string packageName, CancellationToken)` with `public enum StoreOpening { InWindow, OnPhone, StoreMissing, Failed }`.

- [ ] **Step 1: Write the failing tests**: `Les_arguments_ouvrent_l_adresse_sur_l_ecran_virtuel` (expects `["am","start","--user","0","--display","62","--activity-exclude-from-recents","-a","android.intent.action.VIEW","-d","market://details?id=com.makeintouch.dtstuff"]`), `Sans_ecran_virtuel_pas_de_display`, `Un_intent_sans_destinataire_dit_que_le_store_manque` (fake adb output `Error: Activity not started, unable to resolve Intent { ... }`).
- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement** `UriArguments` and `OpenUriAsync`. In `LaunchGameAsync`, when `session.Target.StartUri` is set: no `ForceStopAsync`, call `OpenUriAsync` instead of `LaunchAsync`.
- [ ] **Step 4: Implement `OpenStoreAsync`**: build a transient `DofusInstance` (`PackageName = "com.android.vending"`, `UserId = 0`, `AppLabel = "Play Store - " + Name`, `StartUri = PartnerApps.StoreUri(package)`) and launch it with the existing private `LaunchAsync(instances, remembered: null)`. Find every place that path writes to settings for a launched instance (enabled set, geometry, tabs, playtime) and skip it when `StartUri` is set (Review Focus 1). If a session for this transient key is alive, bring its window forward and return `InWindow` (Review Focus 2). If the launch report opened nothing: `OpenUriAsync(serial, 0, uri, displayId: null)` and return `OnPhone`, or `StoreMissing`.
- [ ] **Step 5: Run the full suite, expect PASS. Commit** — `Open a Play Store page in a window`

### Task 5: Help menu and the card

**Files:**
- Create: `assets/dtstuff.png`, `assets/ocrapp.png` (the `og:image` of each store page, downloaded at 128 px: append `=s128` instead of `=s0-br30`)
- Modify: `src/DtHub.App/DtHub.App.csproj` (two `<Resource Include="..\..\assets\…" Link="assets\…" />`, as for `papycha.png`)
- Modify: `THIRD-PARTY-NOTICES.md` (MakeInTouch's agreement, 2026-10-10, both icons)
- Modify: `src/DtHub.App/Windows/ConfiguratorWindow.xaml:953-981` and `ConfiguratorWindow.xaml.cs`
- Modify: `src/DtHub.App/ViewModels/ConfiguratorViewModel.cs`
- Modify: the three `Strings*.resx`

**Interfaces:**
- Consumes: `PartnerApps.Offered`, `GameLauncher.PartnerAppsInstalled`, `GameLauncher.OpenStoreAsync`, `SettingsService.SetPartnerAppsCardHiddenAsync`.
- Produces: `ConfiguratorViewModel.OfferedPartnerApps` (observable list of `PartnerAppTile`: `Name`, `Line`, `Icon`, `InstallCommand`), `PartnerCardHidden`, `HidePartnerCardCommand`, `ShowPartnerCardCommand`, refreshed after each instance sweep.

- [ ] **Step 1:** Strings, three languages. Keys and French copy: `Help` exists, reuse it, or `DevicesHelp` « Aide »; `PartnerCardHeading` « Les apps de MakeInTouch pour DOFUS Touch »; `PartnerDtStuffLine` « Optimisez vos équipements et créez les meilleurs stuffs. »; `PartnerOcrAppLine` « Partez à la conquête du Dofus Ocre. »; `PartnerInstall` « Installer »; `PartnerCardClose` « Masquer »; `PartnerCardShow` « Afficher les apps de MakeInTouch »; `PartnerNoDevice` « Connectez d'abord un appareil »; `PartnerOnPhone` « Déverrouillez votre téléphone et appuyez sur Installer. »; `PlayStoreMissing` « Le Play Store est absent de cet appareil. »
- [ ] **Step 2:** XAML: the two quiet buttons go into a `ContextMenu` of one `Help` button, same visibility binding. Below, the card (`Card` style, accent tint background), one tile per offered app, close cross top right; the 16 px show-again button when hidden. Several target devices: Install opens a `ContextMenu` of their names.
- [ ] **Step 3:** Build, full suite, then render it with the throwaway WPF program outside the repository, light and dark themes, French, one and two tiles, hidden state. Show the user the captures.
- [ ] **Step 4: Commit** — `Present the MakeInTouch apps under a Help menu`

### Task 6: Compact rows, then the whole thing on a phone

**Files:**
- Modify: `src/DtHub.App/ViewModels/InstanceRowViewModel.cs:50` (`public bool IsPartnerApp => IsShownApp && PartnerApps.IsPartner(Instance.PackageName)`)
- Modify: `src/DtHub.App/Windows/InstanceListControl.xaml` (icon at 424-428 to 16 px, status line hidden, smaller padding, when `IsPartnerApp`)
- Modify: `docs/DECISIONS.md` (D181, French, as the file is)

- [ ] **Step 1:** Triggers in the row template; check that partner rows sort after the game accounts of their device, and fix the order if not.
- [ ] **Step 2:** Build, full suite, render the list with a compact row next to a game row.
- [ ] **Step 3: Manual check on the 13T Pro, with the user:** card shows both apps; Install opens the store window; installing DTStuff makes its compact row appear at the next sweep and DTStuff leave the card; bin on the row, sweep, the row does not come back; quit and relaunch, the store window does not reopen.
- [ ] **Step 4: Commit** — `Show the MakeInTouch apps as compact rows`

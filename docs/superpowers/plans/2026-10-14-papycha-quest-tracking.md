# Papycha quest tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Link DT Hub to the player's Papycha quest tracker and let them follow quests from the guides window: character at the top, automatic "in progress", "Terminée" in one click, marks and filters in the list.

**Architecture:** Pure parsing and rules live in `DtHub.Core/Papycha` (the only layers the tests reference, with Infrastructure). `DtHub.Infrastructure/Papycha/PapychaTracker` talks to the site with the cookie the App layer reads from the guides window's WebView. A thin `QuestTrackerViewModel` in the App holds the state; `QuestViewModel` asks it for marks and filters; `QuestWindow` draws the bar and feeds it steps.

**Tech Stack:** .NET 10, WPF, WebView2, CommunityToolkit.Mvvm, System.Text.Json, xunit.

**Spec:** `docs/superpowers/specs/2026-10-14-papycha-quest-tracking-design.md`

## Global Constraints

- Never reorder the guides list from the site: marks and filters are laid over DT Hub's own order.
- Never extract from Papycha's HTML anything its API or tracker does not hand over; the tracker page is read only for `window.PAPYCHA_QUEST_TRACKER`.
- The access phrase is never seen nor stored by DT Hub; only the cookie `papycha_tracker_code_v2`, kept in the WebView profile.
- Site values, verbatim: tracker page `https://papycha.fr/suivi-quetes/`, ajax `https://papycha.fr/wp-admin/admin-ajax.php`, action `papycha_tracker_quest_status`, statuses `""`, `in_progress`, `completed`, `blocked`, `ignored`.
- Every new label in `Strings.resx` (English), `Strings.fr.resx`, `Strings.es.resx` (Spanish in the familiar register, `tú`); `StringsResourceTests` must stay green.
- New files: UTF-8 with BOM, LF (`.editorconfig`). Comments in English, log messages in French, matching the files around them.
- No new NuGet package.
- Changelogs are written by the release commit, not here.
- Build and test through `rtk proxy` into a log, never trust the exit code alone:
  `rtk proxy dotnet build DtHub.slnx > /tmp/build.log 2>&1; grep -iE "error|avertissement|warning|réussi|succeeded|échec|failed" /tmp/build.log | tail -6`
  `rtk proxy dotnet test tests/DtHub.Tests > /tmp/test.log 2>&1; grep -E "Réussi|Passed|Échec|Failed|total" /tmp/test.log | tail -5`
- Git: `/usr/bin/git`, branch `feature/suivi-papycha`, never `-c user.email`.

## Review Focus

1. **Two quick clicks on "Terminée"**, or a click during a background read: one write only, the button disabled while a write runs (Task 5).
2. **The remembered character was archived or deleted on the site**: fall back to the site's active character, then to the first one (Task 2 `PickCharacter` test).
3. **Phrase renewed while DT Hub is open**: the write fails, the re-read says "not linked", the bar shows "déconnecté"; no retry loop (Task 3 test).
4. **Network down or the site changed**: "Suivi Papycha illisible", guides and list exactly as when not linked, no exception escapes (Task 3 tests, Task 5).
5. **Guides window hidden, WebView asleep, when a cookie is needed**: the cookie read may throw; it is caught and counts as "unreadable" (Task 5).

---

### Task 1: Tracker page parser

**Files:**
- Create: `src/DtHub.Core/Papycha/QuestStatus.cs`
- Create: `src/DtHub.Core/Papycha/TrackerState.cs`
- Create: `src/DtHub.Core/Papycha/TrackerPageParser.cs`
- Modify: `src/DtHub.Core/Papycha/PapychaSite.cs` (constants)
- Test: `tests/DtHub.Tests/Papycha/TrackerPageParserTests.cs`

**Interfaces:**
- Produces:
  - `enum QuestStatus { NotStarted, InProgress, Completed, Blocked, Ignored }`
  - `static class QuestStatusCodes { string ToSite(QuestStatus); QuestStatus FromSite(string?) }`, unknown or empty code gives `NotStarted`.
  - `sealed record TrackerCharacter(string Id, string Name, int Level)`
  - `sealed record TrackerProgress(IReadOnlyDictionary<int, QuestStatus> Statuses, IReadOnlySet<int> Completed, IReadOnlySet<int> Available, IReadOnlyList<int> Next)`
  - `sealed record TrackerState(string Nonce, IReadOnlyList<TrackerCharacter> Characters, string? ActiveCharacterId, IReadOnlyDictionary<string, TrackerProgress> Progress)`
  - `enum TrackerReadOutcome { Linked, NotLinked, Unreadable }`
  - `sealed record TrackerReadResult(TrackerReadOutcome Outcome, TrackerState? State)`
  - `static class TrackerPageParser { TrackerReadResult Parse(string? html) }`
  - In `PapychaSite`: `TrackerUrl = Root + "suivi-quetes/"`, `AjaxUrl = Root + "wp-admin/admin-ajax.php"`, `TrackerCookie = "papycha_tracker_code_v2"`.

- [ ] **Step 1: Write the failing tests**

Fixtures are inline raw strings, cut down from the page captured on 2026-10-14 (see `QuestPageParserTests` for the style). The linked fixture:

```csharp
private const string Linked = """
    <script>var x = 1;</script>
    <script>window.PAPYCHA_QUEST_TRACKER = {"ajaxUrl":"https:\/\/papycha.fr\/wp-admin\/admin-ajax.php","nonce":"26794092b6",
    "space":{"active_character_id":"b","characters":[
      {"id":"a","name":"Vieux","level":50,"archived":true,"quest_statuses":[]},
      {"id":"b","name":"Falcom","level":190,"archived":false,"quest_statuses":{"3692":"completed","47974":"in_progress","100":"weird"}}]},
    "views":{"b":{"progress":{"completed_quests":[3692,3600],"available_quests":[47974,3916],"inferred_quests":[3600]},
      "nextQuests":[3916,{"id":3931,"name":"La potion"},"x"]}},
    "catalog":{"quests":[]}};</script>
    """;
```

Tests, in `TrackerPageParserTests`:
- `Linked_page_gives_state`: outcome `Linked`; `Nonce == "26794092b6"`; one character, `("b","Falcom",190)` (archived dropped); `ActiveCharacterId == "b"`; progress of `"b"`: `Statuses[3692] == Completed`, `Statuses[47974] == InProgress`, `Statuses[100] == NotStarted`; `Completed` = {3692, 3600}; `Available` = {47974, 3916}; `Next` = [3916, 3931].
- `Anonymous_page_is_not_linked`: same script with `"space":null` and `"views":[]` gives `NotLinked`, `State == null`.
- `Page_without_tracker_is_unreadable`: `"<html><body>rien</body></html>"` and `null` give `Unreadable`.
- `Broken_json_is_unreadable`: `window.PAPYCHA_QUEST_TRACKER = {"nonce":` gives `Unreadable`.
- `Empty_statuses_array_is_empty_map`: a character with `"quest_statuses":[]` gives an empty `Statuses`.
- `QuestStatusCodes_round_trip`: `[Theory]` on the five statuses, `FromSite(ToSite(s)) == s`; `ToSite(NotStarted) == ""`, `ToSite(InProgress) == "in_progress"`.

- [ ] **Step 2: Run the tests, see them fail** (compile errors: types missing).

- [ ] **Step 3: Implement**

`Parse`: find `PAPYCHA_QUEST_TRACKER`, then the first `=`, then the first `{` after it; read exactly one JSON value from there with `Utf8JsonReader` over the UTF-8 bytes of the remainder and `JsonDocument.ParseValue(ref reader)` (one value, whatever script follows). Any `JsonException` gives `Unreadable`. `space` null or absent gives `NotLinked`. `quest_statuses` is an object (keys are quest ids as strings) or an empty array. `nextQuests` items are numbers or objects with an `id` number; anything else is skipped. A character without a view gets an empty `TrackerProgress`.

- [ ] **Step 4: Run the tests, see them pass.**

- [ ] **Step 5: Commit** `feat: read the Papycha tracker state from its page`

---

### Task 2: Tracking rules

**Files:**
- Create: `src/DtHub.Core/Papycha/QuestTracking.cs`
- Test: `tests/DtHub.Tests/Papycha/QuestTrackingTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces:
  - `enum QuestMark { None, InProgress, Done, Dimmed }`
  - `static class QuestTracking` with:
    - `QuestStatus StatusOf(TrackerProgress? progress, int questId)`: `Completed` when the quest is in `Completed` (inferred included), otherwise its explicit status, otherwise `NotStarted`.
    - `QuestMark MarkOf(TrackerProgress? progress, int questId)`: Completed gives `Done`, InProgress `InProgress`, Blocked and Ignored `Dimmed`, else `None`; `None` when `progress` is null.
    - `bool Passes(TrackerProgress? progress, int questId, bool toDo, bool available)`: true when `progress` is null; `toDo` drops Completed and Ignored; `available` keeps only ids in `Available`.
    - `bool ShouldStart(QuestStatus status, int stepCount, int index, int resumedIndex)`: true only when `status == NotStarted && stepCount >= 2 && index >= 1 && index != resumedIndex`.
    - `bool IsLastStep(int stepCount, int index)`: `stepCount >= 1 && index == stepCount - 1`.
    - `TrackerCharacter? PickCharacter(IReadOnlyList<TrackerCharacter> characters, string? rememberedId, string? activeId)`: the remembered one if present, else the active one, else the first, else null.

- [ ] **Step 1: Write the failing tests**

One `TrackerProgress` fixture: statuses {1: InProgress, 2: Blocked, 3: Ignored, 4: Completed}, completed {4, 5}, available {1, 6}.
- `MarkOf`: 1 → InProgress, 2 → Dimmed, 3 → Dimmed, 4 → Done, 5 → Done (inferred), 6 → None, null progress → None.
- `Passes`: toDo drops 3, 4, 5 and keeps 1, 2, 6; available keeps 1 and 6 only; both: keeps 1 and 6; null progress always true.
- `ShouldStart`: (NotStarted, 5, 1, 0) true; (NotStarted, 5, 0, 0) false; (NotStarted, 5, 3, 3) false (restored step); (NotStarted, 5, 4, 3) true; (NotStarted, 1, 1, 0) false; (InProgress, 5, 1, 0) false; (Completed, 5, 1, 0) false; (Blocked, 5, 1, 0) false; (Ignored, 5, 1, 0) false.
- `IsLastStep`: (5, 4) true, (5, 3) false, (0, 0) false.
- `PickCharacter`: remembered "b" present gives "b"; remembered "zz" absent and active "a" gives "a"; neither gives the first; empty list gives null.

- [ ] **Step 2: Run, see them fail.**
- [ ] **Step 3: Implement** the static methods above.
- [ ] **Step 4: Run, see them pass.**
- [ ] **Step 5: Commit** `feat: the rules of Papycha quest tracking`

---

### Task 3: Tracker client

**Files:**
- Create: `src/DtHub.Core/Papycha/IPapychaTracker.cs`
- Create: `src/DtHub.Infrastructure/Papycha/PapychaTracker.cs`
- Modify: `src/DtHub.App/AppServices.cs` (registration)
- Test: `tests/DtHub.Tests/Papycha/PapychaTrackerTests.cs`

**Interfaces:**
- Consumes: Task 1 (`TrackerPageParser`, `QuestStatusCodes`, `PapychaSite` constants).
- Produces:
  - `sealed record TrackerWriteResult(bool Success, string? Message, TrackerReadOutcome? After = null)`: `After` is the outcome of the re-read when a retry happened.
  - `interface IPapychaTracker { Task<TrackerReadResult> ReadAsync(string cookie, CancellationToken cancellationToken = default); Task<TrackerWriteResult> SetStatusAsync(string cookie, string characterId, int questId, QuestStatus status, CancellationToken cancellationToken = default); }`
  - `PapychaTracker(HttpClient http, ILogger<PapychaTracker> logger)`, plus `public static IReadOnlyList<KeyValuePair<string, string>> StatusForm(string nonce, string characterId, int questId, QuestStatus status)`.

- [ ] **Step 1: Write the failing tests**

A private `StubHandler : HttpMessageHandler` in the test file, answering from a queue of `(HttpStatusCode, string body)` and recording each request (method, URI, `Cookie` header, form body).
- `StatusForm_has_the_site_fields`: `StatusForm("n1", "b", 3692, Completed)` equals, in order, `action=papycha_tracker_quest_status`, `nonce=n1`, `character_id=b`, `quest_id=3692`, `status=completed`.
- `Read_sends_the_cookie`: one read of the linked fixture (reuse a minimal one) gives `Linked`; the request is `GET https://papycha.fr/suivi-quetes/` with header `Cookie: papycha_tracker_code_v2=abc`.
- `Read_network_failure_is_unreadable`: the handler throws `HttpRequestException`: `Unreadable`, no exception.
- `Write_reads_first_then_posts`: queue = linked page, `{"success":true,"data":{}}`: `Success == true`; two requests, the second a `POST` to the ajax URL whose body has `nonce=26794092b6`.
- `Write_refused_retries_once_after_a_fresh_read`: queue = linked page, `{"success":false,"data":{"message":"Nonce"}}`, linked page, `{"success":true}`: `Success == true`, four requests.
- `Write_after_phrase_renewed_reports_not_linked`: queue = linked page, `{"success":false,"data":{"message":"Accès"}}`, anonymous page: `Success == false`, `After == NotLinked`, three requests, no second post.

- [ ] **Step 2: Run, see them fail.**

- [ ] **Step 3: Implement**

Keeps the last nonce from any read; a write without a nonce reads first. Same User-Agent as `PapychaClient`. Cookie sent as a request header. One retry only, after a fresh read; a re-read that is not `Linked` stops there and returns it in `After`. `HttpRequestException`, `TaskCanceledException` (timeout, not the caller's token) and `JsonException` are caught, logged in French with `[LoggerMessage]`, and give `Unreadable` or `Success == false`.

Registration in `AppServices`, next to `IPapychaClient`: the handler must not keep its own cookies, or the header is dropped:

```csharp
services.AddSingleton<IPapychaTracker>(provider => new PapychaTracker(
    new HttpClient(new SocketsHttpHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) },
    provider.GetRequiredService<ILogger<PapychaTracker>>()));
```

- [ ] **Step 4: Run the tests, see them pass; build the solution.**
- [ ] **Step 5: Commit** `feat: read and write the Papycha tracker`

---

### Task 4: Remembered choices

**Files:**
- Modify: `src/DtHub.Core/Settings/AppSettingsDocument.cs`
- Modify: `src/DtHub.Core/Settings/SettingsService.cs`
- Test: `tests/DtHub.Tests/Settings/SettingsServiceTests.cs`

**Interfaces:**
- Produces: `AppSettingsDocument.PapychaCharacterId` (string, default empty), `QuestFilterToDo` (bool), `QuestFilterAvailable` (bool); `SettingsService.SetPapychaCharacterAsync(string id, CancellationToken = default)` and `SetQuestFiltersAsync(bool toDo, bool available, CancellationToken = default)`, both through `UpdateIfChangedAsync` like `SetLastCalcofusUrlAsync`. No schema bump: new fields with defaults change no old meaning.

- [ ] **Step 1: Failing tests** in the style of the file: each setter persists and reloads; calling it twice with the same value writes once if the file already tests that for another setter.
- [ ] **Step 2: Run, fail. Step 3: Implement. Step 4: Run, pass.**
- [ ] **Step 5: Commit** `feat: remember the Papycha character and the list filters`

---

### Task 5: Tracker view model and linking window

**Files:**
- Create: `src/DtHub.App/ViewModels/QuestTrackerViewModel.cs`
- Modify: `src/DtHub.Core/Dofus/PartnerApps.cs` (`WebTool.PapychaTracker`)
- Modify: `src/DtHub.App/Windows/WebToolWindow.xaml.cs`
- Modify: `src/DtHub.App/Services/WindowPlacements.cs` (`PapychaTracker = "papychatracker"`)
- Modify: `src/DtHub.App/AppServices.cs`
- Modify: the three `Strings*.resx`

**Interfaces:**
- Consumes: Tasks 1 to 4.
- Produces:
  - `enum TrackerLink { Unknown, NotLinked, Linked, Lost, Unreadable }` (in the view model file).
  - `QuestTrackerViewModel(IPapychaTracker tracker, SettingsService settings, ILogger<QuestTrackerViewModel> logger)`, singleton, with:
    - `Func<Task<string?>>? CookieSource` and `Func<Task>? ForgetCookie`, set by `QuestWindow`;
    - observable: `TrackerLink Link`, `bool IsLinked`, `bool ShowsLinkButton` (NotLinked or Lost), `string Notice` (empty, "déconnecté" or "illisible" text), `ObservableCollection<TrackerCharacter> Characters`, `TrackerCharacter? SelectedCharacter`, `bool HasSeveralCharacters`, `TrackerProgress? Progress`, `bool ShowsQuestStatus` (linked and a quest shown), `QuestStatus CurrentStatus`, `string CurrentStatusLabel`, `bool IsDone`, `bool HighlightsDone`, `bool IsBusy`;
    - `Task RefreshAsync()`, `void ShowQuest(int? questId)`, `Task OnStepAsync(int index, int stepCount, int resumedIndex)`, `Task SetStatusAsync(QuestStatus status)`, `Task UnlinkAsync()`;
    - `event EventHandler? ProgressChanged`, raised after every successful read.
  - `WebToolWindow`: `event EventHandler? Linked`.

- [ ] **Step 1: Implement the view model**

- `RefreshAsync`: cookie from `CookieSource` (an exception or null gives `NotLinked` when null, `Unreadable` when it threw); no cookie: `NotLinked`. Then `ReadAsync`: `Linked` fills `Characters`, picks with `QuestTracking.PickCharacter(characters, settings.PapychaCharacterId, state.ActiveCharacterId)`, sets `Progress`; `NotLinked` after a previous `Linked`, or with a cookie present, gives `Lost`; `Unreadable` keeps the last `Progress` out and shows the notice.
- Choosing a character saves it (`SetPapychaCharacterAsync`), switches `Progress` from the last state, raises `ProgressChanged`.
- `ShowQuest(id)` keeps the id, recomputes `CurrentStatus` with `QuestTracking.StatusOf`, `ShowsQuestStatus`, and sets `HighlightsDone` false.
- `OnStepAsync`: `HighlightsDone = !IsDone && QuestTracking.IsLastStep(...)`; when `QuestTracking.ShouldStart(CurrentStatus, stepCount, index, resumedIndex)`, `SetStatusAsync(InProgress)`, once per shown quest.
- `SetStatusAsync`: one write at a time (`SemaphoreSlim(1)`, `IsBusy` true meanwhile, extra calls while busy are dropped); optimistic `CurrentStatus`, put back on failure with the site's message in `Notice`; `After == NotLinked` gives `Lost`; success triggers `RefreshAsync` in the background.
- `UnlinkAsync`: `ForgetCookie`, then `NotLinked`, empty `Characters`, `Progress` null, `ProgressChanged`.
- Logs in French with `[LoggerMessage]`.

- [ ] **Step 2: The linking window**

`WebTool.PapychaTracker`: placement `WindowPlacements.PapychaTracker`, home `PapychaSite.TrackerUrl`, own pages `PapychaSite.Owns(uri)` (others go to the browser through `OnToolNavigating`), title `Strings.Get("TrackerWindowTitle")`. On open, check the cookie with `View.CoreWebView2.CookieManager.GetCookiesAsync(PapychaSite.Root)`; if it was absent, check again on every `NavigationCompleted`, and when `PapychaSite.TrackerCookie` appears raise `Linked` and close. Already linked at opening: it stays open (that is "Ouvrir mon suivi sur Papycha").

`AppServices`: `services.AddSingleton<QuestTrackerViewModel>();` and `services.AddSingleton<Func<WebToolWindow>>(p => () => p.GetRequiredService<WebToolWindow>());`. `ConfiguratorViewModel` must not get a tile for it.

- [ ] **Step 3: Strings**, in the three files (English / French / Spanish):
  `TrackerLink` Link my Papycha tracker / Lier mon suivi Papycha / Vincula tu seguimiento de Papycha;
  `TrackerLost` Papycha tracker disconnected / Suivi Papycha déconnecté / Seguimiento de Papycha desconectado;
  `TrackerUnreadable` Papycha tracker unreadable for now / Suivi Papycha illisible pour le moment / Seguimiento de Papycha ilegible por ahora;
  `TrackerOpen` Open my tracker on Papycha / Ouvrir mon suivi sur Papycha / Abre tu seguimiento en Papycha;
  `TrackerUnlink` Unlink / Délier / Desvincular;
  `TrackerWindowTitle` Papycha tracker / Suivi Papycha / Seguimiento de Papycha;
  `QuestStatusNotStarted` Not started / Non commencée / Sin empezar;
  `QuestStatusInProgress` In progress / En cours / En curso;
  `QuestStatusCompleted` Done / Terminée / Terminada;
  `QuestStatusBlocked` Blocked / Bloquée / Bloqueada;
  `QuestStatusIgnored` Ignored / Ignorée / Ignorada;
  `TrackerDone` Done ✓ / Terminée ✓ / Terminada ✓.

- [ ] **Step 4: Build; run the full suite** (`StringsResourceTests` checks the three files agree).
- [ ] **Step 5: Commit** `feat: link DT Hub to the Papycha tracker`

---

### Task 6: The guides window

**Files:**
- Modify: `src/DtHub.App/Windows/QuestWindow.xaml`
- Modify: `src/DtHub.App/Windows/QuestWindow.xaml.cs`
- Modify: `src/DtHub.App/ViewModels/QuestViewModel.cs` (expose `Tracker`, call `ShowQuest`)
- Modify: `src/DtHub.App/Assets/quest-bridge.js`

**Interfaces:**
- Consumes: Task 5.
- Produces: `QuestViewModel.Tracker` (`QuestTrackerViewModel`, constructor-injected).

- [ ] **Step 1: Wire**

- `QuestViewModel` constructor takes `QuestTrackerViewModel tracker`, exposes it as `Tracker`. `SetCurrent(QuestSummary)` calls `Tracker.ShowQuest(quest.Id)`; the dungeon and path overloads call `Tracker.ShowQuest(null)`.
- `QuestWindow`, once its WebView is ready (where `QuestBridge.Script()` is added): sets `Tracker.CookieSource` (reads `View.CoreWebView2.CookieManager.GetCookiesAsync(PapychaSite.Root)` and returns the value of `PapychaSite.TrackerCookie` or null) and `Tracker.ForgetCookie` (`DeleteCookies(PapychaSite.TrackerCookie, PapychaSite.Root)`), then calls `Tracker.RefreshAsync()`.
- `ResumeStep` keeps the step it resumes in a field `_resumedStep` (0 when none); reset when a new page loads. The `Step` bridge message also calls `_ = _viewModel.Tracker.OnStepAsync(message.Index, _viewModel.StepCount, _resumedStep)`.
- The link button and the "Ouvrir mon suivi" entry open a `WebToolWindow` from the injected `Func<WebToolWindow>` with `Tool = WebTool.PapychaTracker`, one at a time; its `Linked` event calls `Tracker.RefreshAsync()`.
- `quest-bridge.js`: add `'.pqt-quest-actions'` to `HIDDEN`, with a one-line comment that DT Hub's own bar asks the same.

- [ ] **Step 2: The bar** (`DockPanel.Dock="Top"`, right after the search bar, before the quest banner):
  - not linked or lost: the notice if any, and the `TrackerLink` button;
  - linked: a `ComboBox` of `Characters` (`Name` and `Level`) bound to `SelectedCharacter`, replaced by the name alone when `HasSeveralCharacters` is false; a menu button with `TrackerOpen` and `TrackerUnlink`; the notice when not empty.

- [ ] **Step 3: The quest status**, in the quest banner next to the title, visible on `Tracker.ShowsQuestStatus`: `CurrentStatusLabel`; a `QuestStatusCompleted` button (accent style when `HighlightsDone`, disabled when `IsBusy`) that becomes the `TrackerDone` label once `IsDone`; beside it a small menu with the five statuses calling `SetStatusAsync`. Reuse the window's existing styles (`IconButton`, `OutlineToggle`, `RichToolTip`).

- [ ] **Step 4: Build; run the full suite.**
- [ ] **Step 5: Commit** `feat: follow Papycha quests from the guides window`

---

### Task 7: Marks, filters and "Suivantes" in the list

**Files:**
- Modify: `src/DtHub.Core/Papycha/QuestNode.cs` (`QuestMark Mark = QuestMark.None`, last positional parameter)
- Modify: `src/DtHub.Core/Papycha/QuestTree.cs` (`public const int NextSection = -105;`)
- Modify: `src/DtHub.App/ViewModels/QuestViewModel.cs`
- Modify: `src/DtHub.App/Windows/QuestWindow.xaml`
- Modify: the three `Strings*.resx` (`FilterToDo` To do / À faire / Por hacer; `FilterAvailable` Available / Accessibles / Accesibles; `NextQuests` Next / Suivantes / Siguientes)
- Test: `tests/DtHub.Tests/Papycha/QuestTreeTests.cs` (only if `QuestNode` construction there needs the new parameter)

**Interfaces:**
- Consumes: Task 2 (`MarkOf`, `Passes`), Task 4 (filters), Task 5 (`Progress`, `ProgressChanged`).
- Produces: `QuestViewModel.FilterToDo`, `FilterAvailable` (observable, saved through `SetQuestFiltersAsync`, loaded in `InitializeAsync`).

- [ ] **Step 1: Implement**

- One private helper `QuestNode Marked(QuestNode node)` returning `node with { Mark = QuestTracking.MarkOf(Tracker.Progress, node.Quest.Id) }` for quest rows, the node itself otherwise; every place that adds a quest row to `Nodes` goes through it (section list, search, `Suivantes`).
- Section lists (`AddBySuccess(_catalog.InSection(section))`) filter the quests with `QuestTracking.Passes(Tracker.Progress, q.Id, FilterToDo, FilterAvailable)` before `BySuccess`, so empty success headings vanish with them. At the quests root, a branch whose section has no quest passing is skipped. Search is not filtered.
- At the quests root (`RootSection`), when `Tracker.Progress?.Next` names at least one quest of the catalogue, a first branch `NextQuests` with `Id = QuestTree.NextSection`; `ShowSection(NextSection)` lists those quests in the site's order, filtered and marked, breadcrumb "Quêtes > Suivantes", back to `RootSection`.
- `Tracker.ProgressChanged` and a filter change redraw the current level (`ShowSection(_section)`, or the search when `Query` is not empty).
- Template: before `Libelle`, a small glyph for `Mark` (`Done` a check, `InProgress` a dot, in the accent colour), and the row at reduced opacity for `Dimmed`, with `DataTrigger`s on `Mark`.
- The two filters are toggles beside the search box (`OutlineToggle`), visible only when `Tracker.IsLinked`.

- [ ] **Step 2: Build; run the full suite.**
- [ ] **Step 3: Commit** `feat: Papycha marks, filters and next quests in the guides list`

---

## After the tasks

Whole-branch review, full suite, Release build. Then by hand, with the owner, on their account: link, unlink, move through a guide and see "En cours" on the site, mark a quest actually done and see it with its inferred quests, renewed phrase giving "déconnecté". During that check, note the real shape of `nextQuests` and correct Task 1 if it differs.

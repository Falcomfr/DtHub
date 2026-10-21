# DOFUS Touch official names and steps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In English and Spanish, the guides window shows the game's exact names, quest levels, game achievement links and, on demand, a quest's official steps, all read at runtime from DOFUS Touch's data server.

**Architecture:** An embedded table of identifiers pairs Papycha pages with game ids. A Core service reads the game's names for the app's language through a client interface, caches them, and a pure function rewrites a *view* of the Papycha catalog with them; `QuestCatalogService.Catalog` serves that view while the French catalog stays the one that is saved and merged. Official steps are read on demand per quest and shown in place of the WebView.

**Tech Stack:** C# / .NET, WPF, CommunityToolkit.Mvvm, System.Text.Json, xUnit. Python (stdlib only) for the development script.

**Spec:** `docs/superpowers/specs/2026-10-21-dofus-touch-official-names-design.md`

## Global Constraints

- Nothing from the game is embedded: the repository holds only identifiers (`assets/dofus-touch-ids.json`). No game name in tests either: fakes use invented names.
- Endpoint: `POST https://dt-proxy-production-login.ankama-games.com/data/map?lang=<fr|en|es>` with `{"class": "<Class>", "ids": [..]}`; `POST .../data/text?lang=<l>` with `{"ids": [..]}`. Always pass ids, except `QuestObjectiveTypes` (whole class, 35 rows).
- A failure of the game server (exception, non-2xx, malformed JSON) never shows an error: the window behaves as today, in French. Core stores the exception in `LastFailure`; the window logs it once.
- Freshness of cached names: 7 days, like the Papycha catalog.
- Grouped pages join game names with `" / "`.
- Game level used only when `levelMin == levelMax` and `> 0`.
- In French, quest and dungeon titles stay Papycha's; levels, series names and game-only links still apply.
- `Core` references no network or disk (AGENTS.md); `Tests` never touch the network.
- Tests: French snake_case method names, class `XxxTests`, as in `tests/DtHub.Tests/Papycha/`.
- Run tests from `/mnt/c/Dev/DTHub-nomstouch` with
  `rtk proxy dotnet test DtHub.slnx > /tmp/claude-1000/dthub-test.log 2>&1; grep -E "Réussi|Passed|Échec|Failed|error" /tmp/claude-1000/dthub-test.log | tail -5`
  (add `--filter FullyQualifiedName~<Class>` for one class). Read the log, never only the exit code.
- Commits: `git add` by explicit path only (the repository is shared between sessions), check `git show --stat HEAD` after each commit, end messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Path rows under a dungeon in English.** `PathTarget.DungeonOf` matches path titles (French) against dungeon titles: run on translated titles it finds nothing and every "Chemin d'accès" disappears. Test in Task 5.
2. **Prerequisite links in English.** `QuestChainIndex` and `QuestPlayOrder` match prerequisite strings to quest titles; titles and prerequisites must be translated through the same map or chains and previous/next break. Test in Task 4.
3. **Server refuses (403) or is unreachable on first opening.** No cache, client throws: the list must load in French with no error text. Test in Task 3.
4. **Papycha adds a quest the table does not know.** It must show in French, searchable, with no exception. Test in Task 4.
5. **Switching language in the settings.** A cache holding only French names must not show French names as "English": names are cached per language and looked up for the current one. Test in Task 3.

---

### Task 1: Identifier table, its generator and its embedding

**Files:**
- Create: `build/touch-ids/touch_ids.py`, `build/touch-ids/manuel.json`, `assets/dofus-touch-ids.json`
- Create: `src/DtHub.Core/Papycha/TouchIdTable.cs`, `src/DtHub.Core/Papycha/ITouchIdTableSource.cs`
- Create: `src/DtHub.Infrastructure/Papycha/EmbeddedTouchIdTable.cs`
- Modify: `src/DtHub.Infrastructure/DtHub.Infrastructure.csproj:26` (new `EmbeddedResource`, `LogicalName="DtHub.Infrastructure.dofus-touch-ids.json"`)
- Modify: `src/DtHub.App/AppServices.cs:108` (register `ITouchIdTableSource` → `EmbeddedTouchIdTable`)
- Test: `tests/DtHub.Tests/Papycha/EmbeddedTouchIdTableTests.cs`, `tests/DtHub.Tests/Packaging/EmbeddedResourceTests.cs:23-25`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record TouchIdTable
  {
      public Dictionary<int, int[]> Quests { get; init; } = [];             // Papycha quest id → game quest ids, display order
      public Dictionary<int, TouchDungeonIds> Dungeons { get; init; } = []; // Papycha dungeon id → game ids
      public Dictionary<string, int> Series { get; init; } = [];            // Papycha series name → game achievement id
      public Dictionary<int, int> Links { get; init; } = [];                // Papycha quest id → game achievement id (game-only links)
  }
  public sealed record TouchDungeonIds(int[] Dungeons, int Monster = 0);   // Monster: boss, for Moon, Le Chouque, Ul'Khan, Mucane
  public interface ITouchIdTableSource { TouchIdTable Load(); }
  ```
  JSON uses the same options as the catalog (`JsonDocumentStore<T>.SerializerOptions`, camelCase).

- [ ] **Step 1: Write the script and the hand file.** `build/touch-ids/manuel.json` holds the spec's pairings by hand (Papycha id → game ids: 22740→[587], 22201→[616], 13003→[1321], 4775→[132], 4730→[135], 17862→[1614], 37629→[1266], 9903→[60], 14561→[1520], 9687→[650], 16139→[867], 10886→[119], 10963→[1332], 10969→[549], 18714→[1704], 9392→[950], 16022→[868], 13967→[1507], 7044→[934,935], 34217→[4617,4619]), the dungeon pairings (Minotoror→[4], Minotot→[53], each lair title→its "Sombre" and "Céleste" game dungeon ids, Moon/Le Chouque/Ul'Khan/Mucane→monster 226/230/3647/3652), and the refusals (Cavernes Nourricières). `touch_ids.py` reads `assets/papycha-catalog.json`, Papycha's `/quests` and `/dungeons`, and the game classes listed in the spec ("The script reads…"), from the server one request at a time, 60 s apart, stopping at the first non-200, or from a directory given by `--dumps <dir>` (files `touch-<Class>-fr.json`). Matching: exact match of normalized French names (NFKD, no accents, `’`→`'`, `œ`→`oe`, `[..]` and `(..)` removed, leading "quête(s)" removed, non-alphanumerics → space); dungeons also with "Donjon des/du/de la/de" stripped; series by exact normalized name else `difflib` ratio ≥ 0.75; links from `AchievementObjectives.criterion` (`Qf=` / `QF>`) for quests whose catalog `successName` is empty. Hand file wins over matching. Prints counts and the unpaired titles.
- [ ] **Step 2: Generate the table.** Run `python3 -I build/touch-ids/touch_ids.py --dumps /tmp/claude-1000/-home-falcom-dev/c19d47fc-59d3-47ef-ab11-e137e6060d82/scratchpad/dl` (the 2026-10-21 reads; do not query the server again). Expected print: 765 quests, 87 dungeons, 115 series, 50 links. Check the file holds only numbers and Papycha series names.
- [ ] **Step 3: Write the failing tests.** `EmbeddedTouchIdTableTests.La_table_embarquee_apparie_765_quetes_87_donjons_115_series_et_50_liens` asserts those four counts; `La_page_Kapla_Survivant_donne_deux_quetes` asserts `Quests[7044]` is `[934, 935]`; `Cavernes_Nourricieres_n_est_pas_appariee` asserts the Papycha id of "Cavernes Nourricières" is absent. Add `[InlineData("DtHub.Infrastructure.dofus-touch-ids.json")]` to `EmbeddedResourceTests`.
- [ ] **Step 4: Run them, expect FAIL** (type `EmbeddedTouchIdTable` missing).
- [ ] **Step 5: Implement** `TouchIdTable`, `ITouchIdTableSource`, `EmbeddedTouchIdTable` (modelled on `EmbeddedQuestCatalogSnapshot`), the csproj entry and the DI registration.
- [ ] **Step 6: Run the full suite, expect PASS.**
- [ ] **Step 7: Commit** the script, hand file, table, three source files, csproj, AppServices and tests: `feat: embed the Papycha-to-game identifier table`.

### Task 2: Game data client

**Files:**
- Create: `src/DtHub.Core/Papycha/IDofusTouchClient.cs`, `src/DtHub.Infrastructure/Papycha/DofusTouchClient.cs`
- Modify: `src/DtHub.App/AppServices.cs:75` (register like `PapychaClient`, own `HttpClient`, 30 s timeout)
- Test: `tests/DtHub.Tests/Papycha/DofusTouchClientTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IDofusTouchClient
  {
      Task<IReadOnlyDictionary<int, JsonElement>> GetAsync(string dataClass, IReadOnlyCollection<int>? ids, string language, CancellationToken cancellationToken = default);
      Task<IReadOnlyDictionary<int, string>> GetTextsAsync(IReadOnlyCollection<int> ids, string language, CancellationToken cancellationToken = default);
  }
  ```
  Throws on non-2xx or malformed JSON; callers catch. User agent names DT Hub as `PapychaClient` does.

- [ ] **Step 1: Write the failing tests** with a routing `HttpMessageHandler` as in `PapychaClientTests`: `La_requete_poste_la_classe_et_les_ids_dans_la_langue` (asserts POST to `data/map?lang=en`, body `{"class":"Quests","ids":[230]}`, result key 230 with `nameId` "Invented quest"); `Les_textes_se_lisent_par_ids` (`data/text?lang=es`, `{"ids":[1]}` → `{1: "Texto"}`); `Un_refus_403_leve_une_exception`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** `DofusTouchClient(HttpClient http, ILogger<DofusTouchClient> logger)`, base `https://dt-proxy-production-login.ankama-games.com/data/`; omit `ids` from the body when null.
- [ ] **Step 4: Run the suite, expect PASS.**
- [ ] **Step 5: Commit** the two files, AppServices, tests: `feat: read DOFUS Touch's data server`.

### Task 3: Names service and cache

**Files:**
- Create: `src/DtHub.Core/Papycha/TouchNames.cs` (document and records), `src/DtHub.Core/Papycha/TouchNameService.cs`
- Modify: `src/DtHub.Core/Storage/IAppPaths.cs:24`, `src/DtHub.Infrastructure/Storage/AppPaths.cs:22` (`TouchNamesFile` = `Path.Combine(CacheDirectory, "dofus-touch-noms.json")`), `tests/DtHub.Tests/Fakes/FakeAppPaths.cs`
- Modify: `src/DtHub.App/AppServices.cs:59,109` (store + `services.AddSingleton<TouchNameService>()`)
- Test: `tests/DtHub.Tests/Papycha/TouchNameServiceTests.cs`, `tests/DtHub.Tests/Fakes/FakeDofusTouchClient.cs`

**Interfaces:**
- Consumes: `IDofusTouchClient` (Task 2), `ITouchIdTableSource` (Task 1), `IDocumentStore<T>` (existing; `InMemoryDocumentStore` in tests).
- Produces:
  ```csharp
  public sealed class TouchNamesDocument { public Dictionary<string, TouchLanguageNames> Languages { get; set; } = []; }
  public sealed record TouchLanguageNames
  {
      public DateTimeOffset ReadUtc { get; init; }
      public Dictionary<int, TouchQuest> Quests { get; init; } = [];        // by game quest id
      public Dictionary<int, TouchDungeon> Dungeons { get; init; } = [];    // by game dungeon id
      public Dictionary<int, string> Monsters { get; init; } = [];
      public Dictionary<int, string> Items { get; init; } = [];             // dungeon keys
      public Dictionary<int, string> Achievements { get; init; } = [];
      public Dictionary<int, string> ObjectiveTypes { get; init; } = [];    // filled by Task 7
      public Dictionary<int, TouchStep[]> Steps { get; init; } = [];        // by Papycha quest id, filled by Task 7
  }
  public sealed record TouchQuest(string Name, int LevelMin, int LevelMax, int[] StepIds);
  public sealed record TouchDungeon(string Name, int KeyId);
  public sealed record TouchStep(string Name, string Description, TouchObjectiveLine[] Objectives);
  public sealed record TouchObjectiveLine(string Text, string Position);   // Position "[-3,-44]" or ""

  public sealed class TouchNameService(IDofusTouchClient client, IDocumentStore<TouchNamesDocument> store, ITouchIdTableSource table) : IDisposable
  {
      public static string CurrentLanguage { get; }                 // "fr" | "es" | "en" from Strings.Spoken, anything else → "en"
      public TouchIdTable Table { get; }                            // loaded once
      public TimeSpan Freshness { get; init; } = TimeSpan.FromDays(7);
      public Task<TouchLanguageNames?> CachedAsync(string language, CancellationToken ct = default);
      public Task<TouchLanguageNames?> RefreshAsync(string language, CancellationToken ct = default); // fresh cache → no request
      public Exception? LastFailure { get; }
  }
  ```

- [ ] **Step 1: Write the failing tests** (fake client returns invented names and counts calls per class):
  - `Un_premier_rafraichissement_lit_cinq_classes_par_ids_et_les_garde`: RefreshAsync("en") calls `Quests`, `Dungeons`, `Monsters`, `Items`, `Achievements` once each, only with ids from the table (and the dungeons' `dungeonKey`s for `Items`), then `CachedAsync("en")` returns them without a call.
  - `Un_cache_de_moins_de_sept_jours_ne_relit_rien`.
  - `Un_refus_garde_le_cache_et_note_l_echec`: client throws, RefreshAsync returns the previous names (or null when none), `LastFailure` set, no exception.
  - `Les_noms_sont_gardes_par_langue`: after RefreshAsync("fr"), `CachedAsync("en")` is null.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** the records and the service: a `SemaphoreSlim` gate as in `QuestCatalogService`; parse `nameId`, `levelMin`, `levelMax`, `stepIds`, `dungeonKey` from the `JsonElement`s; save the whole document after a successful read.
- [ ] **Step 4: Run the suite, expect PASS.**
- [ ] **Step 5: Commit** the new files, paths, fakes, AppServices, tests: `feat: cache the game's names per language`.

### Task 4: Localized view of the catalog

**Files:**
- Create: `src/DtHub.Core/Papycha/TouchLocalization.cs`
- Test: `tests/DtHub.Tests/Papycha/TouchLocalizationTests.cs`

**Interfaces:**
- Consumes: `TouchIdTable`, `TouchLanguageNames` (Tasks 1, 3), `QuestCatalogDocument`, `QuestSearch.Normalize`.
- Produces: `public static QuestCatalogDocument Apply(QuestCatalogDocument source, TouchIdTable table, TouchLanguageNames names, bool translateNames)`: returns a new document, never mutates `source`.

Rules (copy into the doc comment): titles of paired quests become the game names joined by `" / "` when `translateNames`; `SearchKey` becomes `Normalize(shown + " " + french)`; each prerequisite whose normalized text equals a French quest title takes that quest's shown title; `Level` takes the first game quest's level when precise; `SuccessName` and every `SuccessOrder` entry found in `table.Series` take the game achievement name (all languages); a quest in `table.Links` with an empty `SuccessName` gets that achievement's name and a `ChainStep` after the highest of its series, in Papycha-id order, and the name is appended to `SuccessOrder` if missing; paired dungeons take the joined game dungeon names (or the boss name) when `translateNames`, and their non-empty `Key` the key item name; sections and paths are untouched.

- [ ] **Step 1: Write the failing tests** with an invented two-quest, one-dungeon catalog:
  - `En_anglais_le_titre_et_la_recherche_prennent_le_nom_du_jeu`: title "Invented EN", `QuestSearch.Filter(..., "invented")` finds it and so does the French word.
  - `Une_page_groupee_joint_les_noms_par_une_barre`.
  - `Les_prerequis_suivent_les_titres`: B requires A by French title; after Apply, `new QuestChainIndex(doc.Quests).Find(b.Prerequisites[0])` is A.
  - `En_francais_les_titres_restent_ceux_de_Papycha_mais_les_series_se_corrigent`: `translateNames: false`: title unchanged, `SuccessName` "Cité dor" → game name, `SuccessOrder` too.
  - `Le_niveau_du_jeu_ne_sert_que_s_il_est_precis`: 120/120 → 120; 15/200 → Papycha's 151.
  - `Un_lien_du_jeu_range_la_quete_apres_celles_de_Papycha`.
  - `Une_quete_inconnue_de_la_table_reste_en_francais` (Review Focus 4).
  - `Le_document_source_n_est_pas_modifie`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** with `record with` copies.
- [ ] **Step 4: Run the suite, expect PASS.**
- [ ] **Step 5: Commit** `TouchLocalization.cs` and its tests: `feat: localize a view of the quest catalog with game names`.

### Task 5: Serve the localized catalog

**Files:**
- Modify: `src/DtHub.Core/Papycha/QuestCatalogService.cs` (ctor, `Catalog`, new `Source`, new `RefreshNamesAsync`)
- Modify: `src/DtHub.Core/Papycha/QuestTree.cs:195` (dungeon of a path)
- Modify: `src/DtHub.App/ViewModels/QuestViewModel.cs:415-420` (start the names refresh)
- Modify: `src/DtHub.App/Windows/QuestWindow.xaml.cs` (log `LastFailure` of the names once)
- Test: `tests/DtHub.Tests/Papycha/QuestCatalogServiceTests.cs`, `tests/DtHub.Tests/Papycha/QuestTreeTests.cs`

**Interfaces:**
- Consumes: `TouchNameService` (Task 3), `TouchLocalization.Apply` (Task 4).
- Produces: `QuestCatalogService(IPapychaClient, IDocumentStore<QuestCatalogDocument>, IQuestCatalogSnapshot? snapshot = null, TouchNameService? touch = null)`; `QuestCatalogDocument Source` (French, the one merged and saved); `Catalog` returns the localized view when names are cached for `TouchNameService.CurrentLanguage`, else `Source`; `Task RefreshNamesAsync(CancellationToken ct = default)` refreshes the names and rebuilds the view, never throws.

- [ ] **Step 1: Write the failing tests:**
  - `Le_catalogue_servi_porte_les_noms_du_jeu_le_catalogue_sauve_reste_francais` (QuestCatalogServiceTests).
  - `Sans_noms_en_cache_le_catalogue_reste_celui_de_Papycha` (Review Focus 3: fake client throws, `GetAsync` returns French, no exception).
  - `En_anglais_un_chemin_retrouve_son_donjon` (QuestTreeTests, Review Focus 1): a path "Chemin vers Donjon Inventé" with the dungeon translated still nests under it.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** In the service, rebuild the view after `BaselineAsync` and `RebuildAsync` and in `RefreshNamesAsync`. In `QuestTree`, match `PathTarget.DungeonOf(path.Title, _catalog.Source.Dungeons)` and return the `Catalog` dungeon with the same `Id`. In the view model, after `ShowRoot()` at the end of the load, `_ = _catalog.RefreshNamesAsync()` (names arriving later show at the next opening, per spec).
- [ ] **Step 4: Run the suite, expect PASS.**
- [ ] **Step 5: Commit** the five files and tests: `feat: serve the guides catalog in the game's names`.

### Task 6: Section titles and quest levels in the list

**Files:**
- Create: `src/DtHub.Core/Papycha/QuestSectionTitle.cs`
- Modify: `src/DtHub.Core/Papycha/QuestTree.cs:472` and `src/DtHub.Core/Papycha/QuestSearch.cs:~204` (use `QuestSectionTitle.Of`)
- Modify: `src/DtHub.Core/Localization/Strings.resx`, `Strings.fr.resx`, `Strings.es.resx` (25 keys `QuestSection<id>`)
- Modify: `src/DtHub.Core/Papycha/QuestNode.cs` (level of a row), `src/DtHub.App/Windows/QuestWindow.xaml:950-959`
- Modify: `src/DtHub.Core/Papycha/QuestTree.cs:405-412` (doc comment on the right-hand column: the level is back, on the left)
- Test: `tests/DtHub.Tests/Papycha/QuestSectionTitleTests.cs`, `tests/DtHub.Tests/Papycha/QuestTreeTests.cs`

**Interfaces:**
- Produces: `public static string QuestSectionTitle.Of(QuestSection section)`: when the language is not French, `Strings.Optional("QuestSection" + section.Id)` if present, else `QuestZoneOrder.DisplayName(section.Name)`. `QuestNode.Level` (string): `Facts?.Level`, else the quest's level when `> 0`, else empty; `QuestNode.HasLevelColumn`: `Facts is not null || Quest is not null`.

Resource values (English / Spanish; French = today's `DisplayName`, which `QuestZoneOrder` keeps using for ordering): -2 Main quests / Misiones principales; 659 [Alignment] Bontarian / [Alineamiento] Bontariano; 661 [Alignment] Brakmarian / [Alineamiento] Brakmariano; -3 Krosmoz / Krosmoz; 709 Albuera Archipelago / Archipiélago de Albuera; 18 Astrub / Astrub; 265 Amakna Castle / El castillo de Amakna; 25 Amakna / Amakna; -4 Madrestam Harbour / Puerto de Madrestam; 378 Kwismas Island / Isla de Nawidad; -5 Bworks / Bworks; 151 Pandala Island / Isla de Pandala; 52 Wabbit Island / Isla de los Wabbits; 131 Otomai Island / Isla de Otomai; 135 Frigost Island / Isla de Frigost; -6 Sufokia / Sufokia; 512 Archipelago of Vulkania / Archipiélago de Vulkania; 650 Horrib Isle / Islote Rorífico; -7 Time Bubbles / Burbujas temporales; 657 Bonta & Cania / Bonta y Cania; -8 Sain Ballotin / Sain Ballotin (no game name found, stays French); 668 Orado Island / Isla de Eldoroda; 682 Repeatable quests / Misiones repetibles; -1 Other quests / Otras misiones; 745 The Labyrinth / El Dédalo.

- [ ] **Step 1: Write the failing tests:** `En_anglais_Frigost_s_appelle_Frigost_Island` and `En_francais_le_titre_reste_celui_du_site` (`Strings.Speak` set and reset per test, as existing localization tests do); `La_recherche_trouve_une_rubrique_par_son_nom_anglais` (QuestSearch); `Une_ligne_de_quete_porte_son_niveau` and `Une_quete_sans_niveau_garde_la_colonne` (QuestNode).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement**; in the XAML bind the 22-px level `TextBlock` to `Level` and its visibility to `HasLevelColumn`, so quest labels stay aligned with or without a level.
- [ ] **Step 4: Run the suite, expect PASS** (`StringsResourceTests` checks the three resx share keys).
- [ ] **Step 5: Commit** the files above: `feat: translate section titles and show quest levels`.

### Task 7: Official steps

**Files:**
- Create: `src/DtHub.Core/Papycha/TouchObjectiveText.cs`
- Modify: `src/DtHub.Core/Papycha/TouchNameService.cs` (`StepsAsync`)
- Modify: `src/DtHub.App/ViewModels/QuestViewModel.cs` (steps state, `ShowsPage`), `src/DtHub.App/Windows/QuestWindow.xaml` (button next to `Translate` at 468, steps view next to `View` at 694, credit line), `src/DtHub.App/Windows/QuestWindow.xaml.cs` (button visibility like `Translate`, click)
- Modify: the three `Strings*.resx` (keys below)
- Test: `tests/DtHub.Tests/Papycha/TouchObjectiveTextTests.cs`, `tests/DtHub.Tests/Papycha/TouchNameServiceTests.cs`

**Interfaces:**
- Consumes: `TouchNameService`, `TouchStep`, `TouchObjectiveLine` (Task 3).
- Produces:
  - `public enum TouchHole { Count, Npc, Item, Monster, Area, Text }` and `public static TouchHole TouchObjectiveText.KindOf(int typeId, int hole)` (hole 1-based) per the spec's table: Npc for `#1` of types 1, 2, 3, 9, 12; Item for `#2` of 2, 3 and `#1` of 20; Monster for `#2` of 6, 12 and `#1` of 14, 45, 46; Area for `#1` of 5; Text for `#1` of 0, 4, 10; otherwise Count.
  - `public static string TouchObjectiveText.Fill(string template, int typeId, IReadOnlyList<int> parameters, Func<TouchHole, int, string> name)`: replaces `#1..#3` with `parameters[0..2]`, through `name` unless Count; a missing name leaves the hole's number.
  - `public Task<IReadOnlyList<TouchStep>> TouchNameService.StepsAsync(int papychaQuestId, string language, CancellationToken ct = default)`: cached steps first; else reads `QuestSteps` (step ids of the paired game quests, in order), `QuestObjectives`, `QuestObjectiveTypes` (once per language, kept in `ObjectiveTypes`), then names by kind (`Npcs`, `Items`, `Monsters`, `SubAreas`, `data/text`), only for non-empty id sets; caches and saves; empty list when unpaired or on failure (`LastFailure` set).
  - View model: `bool OffersOfficialSteps` (language ≠ fr and the open guide is a paired quest), `[ObservableProperty] bool showsOfficialSteps` (reset to false when another guide opens; `ShowsPage` also requires `!ShowsOfficialSteps`), `IReadOnlyList<TouchStep> OfficialSteps`, `Task ToggleOfficialStepsAsync()`.
  - Resx keys (en / fr / es): `OfficialSteps` Official steps / Étapes officielles / Pasos oficiales; `OfficialStepsTip` The quest's steps as the game words them / Les étapes de la quête telles que le jeu les écrit / Los pasos de la misión tal como los escribe el juego; `BackToGuide` Papycha guide / Guide Papycha / Guía de Papycha; `OfficialStepsLoading` Reading the game's steps… / Lecture des étapes du jeu… / Leyendo los pasos del juego…; `OfficialStepsUnavailable` The game's steps could not be read. / Les étapes du jeu n'ont pas pu être lues. / No se pudieron leer los pasos del juego.; `OfficialNamesCredit` Names and steps: DOFUS Touch, Ankama / Noms et étapes : DOFUS Touch, Ankama / Nombres y pasos: DOFUS Touch, Ankama.

- [ ] **Step 1: Write the failing tests:** `Aller_voir_prend_le_nom_du_PNJ` ("Talk to #1", type 1, [447] → "Talk to Invented NPC"); `Ramener_des_objets_remplit_les_trois_trous` (type 3, "Take #3 #2 to #1", [119, 1746, 2] → "Take 2 Invented item to Invented NPC"); `Un_texte_libre_vient_des_textes` (type 0, "#1", [662825]); `Un_nom_manquant_laisse_le_numero`; `Les_etapes_d_une_page_groupee_se_suivent` and `Les_etapes_lues_ne_se_relisent_pas` and `Une_quete_non_appariee_n_a_pas_d_etapes` (service, fake client).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** the formatter and `StepsAsync`. Objective position: `"[x,y]"` from `coords` when it has `x` and `y`, else empty.
- [ ] **Step 4: Implement the window part.** The button sits left of `Translate`, visible when `OffersOfficialSteps`; it reads `OfficialSteps` while showing `OfficialStepsLoading`, then swaps the WebView for a scrolling list (step name in bold, description, one line per objective with its position in a muted right column), with `BackToGuide` to return and `OfficialNamesCredit` muted at the bottom; `OfficialStepsUnavailable` when the list comes back empty. No hard-coded text (`Conventions/DisplayedTextTests`).
- [ ] **Step 5: Run the suite, expect PASS.**
- [ ] **Step 6: Commit** the files above: `feat: show a quest's official steps in English and Spanish`.

### Task 8: Credit, notices, decision, release step

**Files:**
- Modify: `src/DtHub.App/Windows/QuestWindow.xaml` (muted `OfficialNamesCredit` under the list when the language is not French)
- Modify: `THIRD-PARTY-NOTICES.md` (summary table row + section "Names and steps from DOFUS Touch's game server", after "The Almanax on Ankama's portal")
- Modify: `docs/DECISIONS.md` (append `## D208 - Les noms du jeu sont lus chez le joueur, rien n'est embarqué`, dated 2026-10-21, French, in the file's style)
- Modify: `docs/LIVRAISON.md:22-25` (after the Papycha probe: rerun `build/touch-ids/touch_ids.py` from a home connection, commit the table if it changed)

- [ ] **Step 1: Write the notices section:** source (endpoint, read on the player's PC in their language, cached 7 days), nothing embedded but `assets/dofus-touch-ids.json` (DT Hub's own identifiers, no game text), credit shown in the window, failure falls back to Papycha's French.
- [ ] **Step 2: Write D208:** the rule of AGENTS.md, the Almanax precedent, the choice (recommended by Claude, accepted by the user), the 403 of 2026-10-21 and the budget it dictates.
- [ ] **Step 3: Add the credit line and the release step.**
- [ ] **Step 4: Run the suite, expect PASS; build `DtHub.slnx`** (`rtk proxy dotnet build DtHub.slnx > /tmp/claude-1000/dthub-build.log 2>&1`, then grep `error|erreur|warning|avertissement` in the log) with 0 errors and 0 warnings.
- [ ] **Step 5: Commit:** `docs: credit the game's names and record D208`.

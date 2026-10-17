# Papycha public API merge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The quest catalog reads `papycha-public/v1` and merges it over the catalog DT Hub already has (cache, or an embedded 11/10 snapshot), never removing anything.

**Architecture:** A pure `QuestCatalogMerge` in Core applies API quests, dungeons and paths onto a base `QuestCatalogDocument` by id. `QuestCatalogService` picks the base (cache, else embedded snapshot), decides staleness from `/status`, merges and saves. `PapychaClient` is rewritten on the public API. The wp/v2-only code (Arrange, success seed, page parsers, probes reading article bodies) is removed.

**Tech Stack:** .NET 10, C#, System.Text.Json, xUnit. From WSL: `dotnet.exe`, run from `/mnt/c/Dev/DTHub-papyapi`.

**Spec:** `docs/superpowers/specs/2026-10-17-papycha-public-api-merge-design.md`

## Global Constraints

- Repository language English; test method names French sentences with underscores; logs French; no em dash.
- `Core` references nothing (no network, no disk). `Tests` never touch the network.
- Every `catch (Exception)` has a `when` filter (`CatchDisciplineTests`).
- A visible string goes in all three `Strings*.resx` or none.
- Build green and tests green at every commit; zero warnings; UTF-8, LF.
- Stage files by explicit path (shared repository); check `git show --stat` before each commit.
- API base `https://papycha.fr/wp-json/papycha-public/v1/`, `per_page=100`, User-Agent unchanged.
- Never remove a quest, dungeon or path absent from the API; never overwrite a base value with an empty API value.

## Review Focus

- A quest the API returns with a section id unknown to the base and a zone id known to it: filed under the zone's section, not under "Autres quêtes". Test in Task 2.
- The API sends `level: null` and `start_coordinates: null`: the base's level and position survive. Test in Task 2.
- A paged response whose `total_pages` is 0 or missing: the client stops after the first page instead of looping. Test in Task 3.
- An existing player's cache (schema 16, wp/v2 stamp fields set): first open after the update merges once, then stays quiet within `Patience`. Test in Task 4.
- HTML entities in names (`&rsquo;`, `&amp;`): decoded in titles and search keys. Test in Task 3.

---

### Task 1: Embedded snapshot

**Files:**
- Create: `assets/papycha-catalog.json` (from `%LOCALAPPDATA%\DtHub\cache\papycha-quetes.json` of 2026-10-11, minified, `siteModifiedUtc` null, `sitePosts` 0, `siteCategories` empty, `needsRebuild` dropped)
- Create: `src/DtHub.Core/Papycha/IQuestCatalogSnapshot.cs`
- Create: `src/DtHub.Infrastructure/Papycha/EmbeddedQuestCatalogSnapshot.cs`
- Modify: `src/DtHub.Infrastructure/DtHub.Infrastructure.csproj` (EmbeddedResource, LogicalName `DtHub.Infrastructure.papycha-catalog.json`)
- Test: `tests/DtHub.Tests/Papycha/EmbeddedQuestCatalogSnapshotTests.cs`

**Interfaces:**
- Produces: `public interface IQuestCatalogSnapshot { QuestCatalogDocument Load(); }`; `public sealed partial class EmbeddedQuestCatalogSnapshot(ILogger<EmbeddedQuestCatalogSnapshot> logger) : IQuestCatalogSnapshot`, deserializing with `JsonDocumentStore<QuestCatalogDocument>.SerializerOptions`, returning a new empty document (logged) if the resource is missing or unreadable. Each call returns a fresh instance (callers mutate lists).

- [ ] **Step 1:** Test `Le_catalogue_embarque_contient_les_782_quetes_du_11_octobre`: `Load()` has 782 quests, 25 sections, 93 dungeons, 22 paths, `SchemaVersion == QuestCatalogDocument.CurrentSchemaVersion`, `SiteModifiedUtc == null`, `NeedsRebuild == false`; quest 14099 has `SectionIds` containing 668.
- [ ] **Step 2:** Run, expect FAIL (type missing).
- [ ] **Step 3:** Produce the asset with a one-off Python command, add interface, class, csproj entry.
- [ ] **Step 4:** Run the test, expect PASS.
- [ ] **Step 5:** Commit `feat: embed the 11 October quest catalog as a fallback`.

### Task 2: Pure merge

**Files:**
- Create: `src/DtHub.Core/Papycha/QuestCatalogMerge.cs`
- Test: `tests/DtHub.Tests/Papycha/QuestCatalogMergeTests.cs`

**Interfaces:**
- Produces: `public static QuestCatalogDocument QuestCatalogMerge.Apply(QuestCatalogDocument baseline, IReadOnlyList<QuestSummary> quests, IReadOnlyList<DungeonSummary> dungeons, IReadOnlyList<PathSummary> paths, DateTimeOffset? siteModified, DateTimeOffset now)`. Returns a new document; `baseline` is not mutated. API quests arrive as `QuestSummary` with `Categories` = API section ids then zone ids, `Prerequisites` = access conditions then previous quest names, `SearchKey` set. API paths arrive with `Side` unset (decided here with `PathTarget.Of(title, dungeon titles of kind Dungeon)`).
- Moves `QuestCatalogService.OtherSectionId` (-1) here as `public const int OtherSectionId = -1;` and keeps `QuestCatalogService.OtherSectionId => QuestCatalogMerge.OtherSectionId` only if other code references it.

Rules exactly as in the spec "Merge rules". Root category 7 never files a quest. New quest `SectionId` = its section with the smallest merged `Count`, ties by id. Section `Count` recomputed from merged `SectionIds`. "Autres quêtes" section (`Id -1`, name `Autres quêtes`) appended if a new quest needs it and the base lacks it. `IndexedUtc = now`, `SiteModifiedUtc = siteModified`. `SectionOrder`, `SuccessOrder` copied.

- [ ] **Step 1:** Tests (French names), each on a small hand-built baseline:
  - `Une_quete_connue_prend_le_titre_l_adresse_le_niveau_et_le_depart_de_l_api_et_garde_le_reste` (SectionIds, SuccessName, PlayOrder, StartPerson unchanged; Title, Url, Level, StartPosition, SearchKey from API)
  - `Un_champ_vide_de_l_api_n_efface_pas_la_valeur_connue` (Level 0, StartPosition "" from API keep 118 / "[4,-6]")
  - `Les_prerequis_connus_l_emportent_et_ceux_de_l_api_comblent_un_vide`
  - `Une_quete_nouvelle_rejoint_sa_rubrique_connue` (category 25 known → SectionIds [25], Count of 25 +1)
  - `Une_quete_nouvelle_rejoint_la_rubrique_de_sa_zone_quand_sa_rubrique_est_inconnue` (Categories [724, 18], 18 known → [18])
  - `Une_quete_nouvelle_sans_rubrique_connue_va_dans_autres_quetes_creee_au_besoin`
  - `Une_quete_absente_de_l_api_est_gardee`
  - `Un_donjon_est_rafraichi_ajoute_et_jamais_retire`
  - `Un_chemin_nouveau_prend_son_cote_d_apres_les_donjons` and `Un_chemin_connu_garde_son_cote`
  - `La_fusion_ne_modifie_pas_le_catalogue_de_depart`
- [ ] **Step 2:** Run, expect FAIL.
- [ ] **Step 3:** Implement `Apply`.
- [ ] **Step 4:** Run, expect PASS.
- [ ] **Step 5:** Commit `feat: merge Papycha's public API over the known catalog`.

### Task 3: Client on the public API

**Files:**
- Modify (rewrite): `src/DtHub.Infrastructure/Papycha/PapychaClient.cs`
- Modify: `src/DtHub.Core/Papycha/IPapychaClient.cs`
- Test: `tests/DtHub.Tests/Papycha/PapychaClientTests.cs` (routing handler local to the file: path+query → JSON bytes)

**Interfaces:**
- Produces `IPapychaClient`:
  - `Task<IReadOnlyList<QuestSummary>> GetQuestsAsync(IProgress<QuestIndexingProgress>? progress = null, CancellationToken cancellationToken = default)` (reports `Loaded`/`Total` from `total`)
  - `Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(CancellationToken cancellationToken = default)` (kind `dungeon`/`raid`/`lair` → `DungeonKind`, title prefix `[...]` stripped, key name, `soul_stone_size`, `contact_npc`, `entrance_coordinates`)
  - `Task<IReadOnlyList<PathSummary>> GetPathsAsync(CancellationToken cancellationToken = default)` (title prefix stripped)
  - `Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default)` (`/status` `last_updated`, `Posts` 0; `null` on any failure, logged)
- Removed: `GetSectionsAsync`, `GetPageSectionsAsync`, `GetCategoryStampsAsync`, the `dungeonTitles` parameter. `QuestIndexingPhase.Sections` removed.
- Listing failures (non-success status, bad JSON) throw `HttpRequestException`/`JsonException`, caught by the service as today.

- [ ] **Step 1:** Tests: `Les_quetes_sont_lues_page_par_page_et_converties` (two pages, ids, decoded `L’île`, level null → 0, Categories = section ids then zone ids, Prerequisites = conditions then previous names, Url); `Un_nom_avec_des_entites_html_est_decode` (`Bonta &amp; Cania` → `Bonta & Cania`, SearchKey normalized); `Une_reponse_sans_nombre_de_pages_s_arrete_a_la_premiere`; `Les_donjons_perdent_leur_prefixe_et_gardent_clef_et_pierre`; `Le_tampon_vient_du_statut` and `Un_statut_illisible_rend_null`.
- [ ] **Step 2:** Run, expect FAIL.
- [ ] **Step 3:** Rewrite the client (one private `ReadAllAsync<T>(family, ...)` pager).
- [ ] **Step 4:** Do not commit yet: Core and tests do not build until Task 4.

### Task 4: Service on the merge, removal of the wp/v2 path

**Files:**
- Modify: `src/DtHub.Core/Papycha/QuestCatalogService.cs` (constructor `(IPapychaClient client, IDocumentStore<QuestCatalogDocument> store, IQuestCatalogSnapshot? snapshot = null)`; delete Arrange and helpers, SuccessOrder, Widen, PageUrls, ExtraSections, MatchingCategory, Successes, Claims, Order, PrimarySection, Moved)
- Modify: `src/DtHub.Core/Papycha/QuestCatalogDocument.cs` (doc comments for the new stamp meaning; `SiteCategories`/`SitePosts` removed if nothing else reads them; schema stays 16)
- Modify: `src/DtHub.App/AppServices.cs` (register `IQuestCatalogSnapshot, EmbeddedQuestCatalogSnapshot`; drop the seed)
- Modify: `src/DtHub.Core/Papycha/QuestIndexingLabel.cs`, three `Strings*.resx` (`IndexingSections` removed)
- Delete: `IQuestSuccessSeed.cs`, `EmbeddedQuestSuccessSeed.cs`, `assets/quest-successes.json` + its csproj line, `build/extract-successes.py`, `tests/.../FakeQuestSuccessSeed.cs`, and every parser/type whose only users were the deleted code (`QuestMenuParser`, `QuestSectionPageParser`, `QuestPageSection`, `DungeonPageParser`, `CategoryStamp` as the build shows) with their tests
- Modify: `tests/DtHub.Tests/Fakes/FakePapychaClient.cs` (quests, dungeons, paths, stamp; `Failure`, `Calls`, `StampCalls`)
- Modify: `tests/DtHub.Tests/Papycha/QuestCatalogServiceTests.cs`, `QuestTreeTests.cs` (tree tests seed the store with a document instead of relying on the client's sections)

**Interfaces:**
- Consumes: Task 1 `IQuestCatalogSnapshot`, Task 2 `QuestCatalogMerge.Apply`, Task 3 `IPapychaClient`.
- `GetAsync`: `_current ??= store.Load`; if `_current.NeedsRebuild`, `_current = snapshot?.Load() ?? new()`. Stale rules from the spec ("When to ask the API"); `IndexedUtc` null counts as stale. `RebuildAsync`: quests (zero → keep base), dungeons, paths, stamp, `Apply`, save, `LastIndexing`; failure → base kept, `LastFailure` set. Progress phases: Quests, Dungeons, Paths, Arranging.

- [ ] **Step 1:** Service tests: keep and adapt the staleness and failure tests to the stamp; add `Sans_cache_le_catalogue_embarque_sert_de_base_et_est_fusionne`, `Sans_cache_et_sans_reseau_le_catalogue_embarque_est_rendu`, `Un_cache_existant_est_fusionne_une_fois_puis_laisse_tranquille` (schema 16 doc with old stamp → one merge; reopen within Patience → `StampCalls` and `Calls` unchanged), `Un_statut_inchange_ne_provoque_aucune_fusion`, `Un_statut_qui_bouge_provoque_une_fusion`. Delete the tests of removed behavior (sections from pages, achievement order, widening, seed).
- [ ] **Step 2:** Run, expect FAIL.
- [ ] **Step 3:** Implement, delete, rewire.
- [ ] **Step 4:** `dotnet.exe build DtHub.slnx` zero warnings; `dotnet.exe test DtHub.slnx` all green; `dotnet.exe format DtHub.slnx --verify-no-changes`.
- [ ] **Step 5:** Commit Tasks 3 and 4 together `feat: read the catalog from Papycha's public API`.

### Task 5: Probe, docs, notices

**Files:**
- Modify (rewrite): `build/sonde-papycha/Program.cs`, `build/sonde-papycha/reference.json` (re-blessed)
- Delete: `build/sonde-rang/`, `build/sonde-voisines/`, `build/sonde-papycha/audit-etapes.mjs`; their entries in `DtHub.slnx`
- Modify: `AGENTS.md` (commands), `THIRD-PARTY-NOTICES.md` (snapshot entry replaces the seed entry), `docs/LIVRAISON.md` step 0, `docs/DECISIONS.md` (French entry), `docs/IMPLEMENTATION_STATUS.md`, `CHANGELOG.md`/`.fr.md`/`.es.md` (`## [Unreleased]`, one line each under Fixed: the Guides tab fills again, new quests arrive by themselves)

Probe measures: `statut-active` (1/0), `quetes`, `donjons`, `raids`, `tanieres`, `chemins`, `quetes-avec-niveau`, `quetes-avec-depart`, `quetes-du-catalogue-embarque-encore-servies`, `lieux-du-catalogue-embarque-encore-servis`; requires the status enabled and quests > 0; compares measures to `reference.json` as today (`--benir`).

- [ ] **Step 1:** Rewrite the probe, run `dotnet.exe run --project build/sonde-papycha -- --benir`, then without `--benir`, expect exit 0.
- [ ] **Step 2:** Docs and notices.
- [ ] **Step 3:** Build, test, format as Task 4 Step 4.
- [ ] **Step 4:** Commit `chore: probe the public API and drop the wp/v2 tools`.

### Task 6: Verification in the application

- [ ] **Step 1:** Publish to `build/publish`. Copy `%LOCALAPPDATA%\DtHub\cache\papycha-quetes.json` aside and delete it, launch, open the guides, check the Guides tab is full and the log shows a merge; restore the file. Then launch on the existing cache and check the 5 new quests are findable (search "Bellaphones").
- [ ] **Step 2:** Whole-branch review by a fresh reviewer; fix findings; rerun Task 4 Step 4.

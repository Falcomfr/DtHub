# Guides tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dungeon value, team recommendations, where to find quest items, archimonsters, itinerary by zone and the whole-level line, in the guides window.

**Architecture:** Three independent Core/Infrastructure pieces built in parallel in separate worktrees (A: item sources and archimonsters; B: team, itinerary and level maths; C: dungeon value with objective texts and the XP table in the facts read), merged, then one UI task (D) wiring them into the window, then records (E).

**Tech Stack:** .NET 10, WPF, xunit; build/test through `rtk proxy dotnet … > log` (CLAUDE.md).

**Spec:** `docs/superpowers/specs/2026-10-23-guides-tools-design.md` (builds on D218, D219).

## Global Constraints

- Nothing from the game embedded; tests use invented values.
- Game server: never a whole `Monsters` or `Items` class; ids only, on demand, cached; during development at most 10 real requests per task, 60 s apart, from the home connection (VPN addresses get 403).
- A, B, C add no user-visible strings (no resx edits): they return data; D formats it. Avoids merge conflicts.
- Unknown data hides a hint or a value, never a guess.
- New .cs UTF-8 with BOM; no em dash anywhere; `/usr/bin/git`, add files by name, conventional subjects, trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- Item with no drop, no harvest, no craft (Marron Glacé): "event or shop", not an empty tooltip (A test).
- Archimonster soul whose monster lacks sub-areas: name shown, no area list (A test).
- Team with one linked character only: team view empty, no crash (B test).
- Level 200 or XP table missing: no level line (B test).
- Dungeon name that is a substring of another ("Donjon des Bouftous" / "Donjon des Bouftous Royaux"): longest match wins (C test).

---

### Task A: Item sources and archimonsters (worktree A, new files only)

**Files:** Create `src/DtHub.Core/Papycha/ItemSources.cs` (records, interface), `src/DtHub.Infrastructure/Papycha/ItemSourceService.cs` (or Core if it only needs `IDofusTouchClient`, like TouchNameService), tests `tests/DtHub.Tests/Papycha/ItemSourceServiceTests.cs`. Registration in the App's DI next to TouchNameService (one line in the composition root).

**Produces:**
- `public sealed record MonsterSource(int Id, string Name, int Level, bool IsArchimonster, string? NormalName, IReadOnlyList<string> SubAreas);`
- `public sealed record ItemSource(IReadOnlyList<MonsterSource> Droppers, IReadOnlyList<(string Job, int Level)> Harvest, IReadOnlyList<(string Job, int Level)> Craft, bool NoKnownSource);`
- `public sealed class ItemSourceService { Task<ItemSource?> SourceAsync(int itemId, string language, CancellationToken ct = default); }` - reads `Items` [itemId] (`dropMonsterIds`, at most 5 kept, highest-level first or as given: builder's call), `Monsters` for those ids (name, max grade level, `isMiniBoss`, `subareas`, the normal counterpart for an archimonster), `SubAreas` names (at most 5), harvest via `Skills` (gathered item -> job and level; probe the class size first, read it whole only if under 200 KB, else by ids), craft via `Recipes` (result item -> job and level); job names via `Jobs`. Cached per language on disk like TouchNamesDocument (own document), 30 days. Never throws except on cancellation; null on failure.
- Tests with `FakeDofusTouchClient.Rows`: dropper with sub-areas; archimonster with normal counterpart; harvest; craft; no source -> `NoKnownSource`; cache hit asks nothing; failure -> null.
- Commit `feat: find where quest items come from, archimonsters included`.

### Task B: Team, itinerary and levels (worktree B, new file only)

**Files:** Create `src/DtHub.Core/Papycha/QuestPlanning.cs`, tests `tests/DtHub.Tests/Papycha/QuestPlanningTests.cs`. Read-only use of `QuestProfit` (do not change it: task C does).

**Produces:**
- `public sealed record TeamEntry(ProfitEntry Entry, IReadOnlyList<TrackerCharacter> Characters, QuestGain Gain, double Score, int Tier);`
- `QuestPlanning.Team(IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, TouchQuestFacts> facts, IReadOnlyList<(TrackerCharacter Character, TrackerProgress Progress)> characters, int bonusPercent) : IReadOnlyList<TeamEntry>` - per character, `QuestProfit.Score` + `Entries` at the character's own level; group entries by identity (same quest ids); keep those doable by >= 2 characters; gain summed over them; score = summed combined score; tiers like QuestProfit's (10/15/25 %).
- `public sealed record AreaGroup(string Area, IReadOnlyList<ProfitEntry> Entries, double Score);`
- `QuestPlanning.ByArea(IReadOnlyList<ProfitEntry> entries, Func<QuestSummary, string> areaOf) : IReadOnlyList<AreaGroup>` - area of an entry = area of its first quest; groups by summed score desc, entries inside keep their order.
- `public sealed record LevelLine(int Count, int From, int To);`
- `QuestPlanning.WholeLevel(IReadOnlyList<ProfitEntry> ordered, int level, IReadOnlyDictionary<int, long> totalXpByLevel) : LevelLine?` - smallest N such that the first N entries' summed XP >= totalXp[level+1] - totalXp[level]; null at 200, when the table lacks a level, or when no N reaches it.
- Tests for each, plus one character only -> empty team; level 200 -> null.
- Commit `feat: team, area and level planning for recommended quests`.

### Task C: Dungeon value, objective texts, XP table (worktree C)

**Files:** `src/DtHub.Core/Papycha/TouchNames.cs`, `TouchNameService.cs`, `QuestProfit.cs`, their tests.

**Produces:** the dungeon brief at `docs/superpowers/plans/2026-10-23-guides-tools-dungeon-brief.md` (Core part only: points 1 to 3; display and docs belong to D and E), plus:
- `TouchLanguageNames.Experience : Dictionary<int, long>` (level -> total XP) read with the facts from `Experiences` (whole class, about 200 rows; probe its field names with one request) as one more request, same pause and back-off.
- `QuestProfit.DungeonsOf(...)` and `public sealed record DungeonValue(int PapychaDungeonId, IReadOnlyList<ProfitEntry> Advances, QuestGain Gain, double Score, int Tier);` `QuestProfit.DungeonValues(...) : IReadOnlyDictionary<int, DungeonValue>` (signature at the builder's discretion, documented in the report).
- Commit `feat: value dungeons by the quests they advance`.

### Task D: Wire everything into the window (main worktree, after A, B, C are merged)

QuestViewModel / QuestTrackerViewModel / QuestWindow.xaml / resx x3:
- Dungeon rows: priority mark, both gains with icons, tooltip "Fait avancer n quête(s) : …"; dungeons mixed into "Recommandées".
- "Recommandées": a two-state "Moi / Équipe" toggle and a "Par zone" grouping toggle; team rows show "n persos" and names in the tooltip; zone headers show area and count.
- The whole-level line above the list.
- "Where to find" on quest objectives asking items: in the gain/reward tooltip of the row and under the reward line of an open guide: "Lâché par …", "Récolte : Bûcheron niv. 100", "Fabrication : …", "Archimonstre : … (version normale : …), zones : …", "Événement ou boutique"; read on hover / guide open via ItemSourceService.
- Strings in three languages. Build, full suite, format. Commit `feat: guides tools in the window`.

### Task E: Records

D220 (French): dungeon value rule, team, where to find, archimonsters, itinerary, level line, the extra requests. Notices: Items, Monsters, SubAreas, Skills, Recipes, Jobs, Experiences read by ids on demand, nothing embedded. Changelogs x3. Commit `docs: record guides tools`.

### Task F: Mark many quests done at once (after D)

Asked by the user: "un outil pour valider rapidement les quêtes pour ceux qui ont déjà beaucoup de quêtes à valider d'un coup,
dans les listes pouvoir valider rapidement les lots". Lesson from the 2026-10-23 import: papycha.fr answers success but loses
about one write in seven when writes follow each other quickly; refusals for the "Dévotion à …" quests ("Statut invalide").

- Core: `QuestBatch` plan + runner: given quest ids and a status, write them one by one through `IPapychaTracker.SetStatusAsync`
  with a pause (1.8-3 s, injectable), then re-read the tracker, retry what did not hold (at most 3 extra rounds), report done /
  refused (with the site's message) / still missing; cancellable; progress callback. Tests with a fake tracker that drops some
  writes and refuses one.
- UI: a "Sélection" toggle in the list toolbar shows a checkbox on quest rows (Shift+click selects a range); achievement and
  zone headers get a "Tout marquer terminé" action (quests not done, repeatables excluded); a "Marquer terminées (n)" button
  with a confirmation when n > 20; a progress line "42 / 140" with Cancel; at the end a summary line (done, refused, missing).
- Strings x3, decision note in D220, changelogs. Commit `feat: mark many quests done at once`.

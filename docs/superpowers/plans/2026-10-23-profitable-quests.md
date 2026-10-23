# Profitable quests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show each quest's gain in every list, a three-tier priority mark on doable quests, and a "Quêtes rentables" entry ranking doable quests and series by gain for effort, by XP or by kamas, for the chosen character.

**Architecture:** A bulk read in `TouchNameService` caches every paired quest's step rewards and objectives. Pure Core code computes effort, doability, scores, tiers and the ranked entry. `QuestViewModel.Marked` lays gain and tier on every quest row; a new special section lists the ranking; the level and Bonus Pack boxes show in every view, with an XP/kamas switch.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xunit; build/test through `rtk proxy dotnet … > log` (see `CLAUDE.md`).

**Spec:** `docs/superpowers/specs/2026-10-23-profitable-quests-design.md` (builds on D218).

## Global Constraints

- Nothing from the game embedded (D212); tests use invented values.
- Bulk read: `QuestSteps`, `QuestObjectives`, `QuestStepRewards` for every paired quest, at most three requests, at least 60 s apart, in the background, never on the UI thread; a refusal keeps the cache; 7-day freshness like the names; no `Monsters`.
- Doable: status not Completed/Blocked/Ignored; in the character's `TrackerProgress.Available` or status InProgress; recurrence Once or Unknown (and not filed under the repeatables section, the rule `QuestTracking.ZoneProgress` already uses); first step's optimal level <= character level.
- Effort per objective type: 1/9/2 -> 1; 4 -> 1.5; 3 -> 2 + quantity/10, max 7; 20 -> 3; 6/14 -> 3, 8 when the objective is "1 in one fight" (type 6 with count 1), +4 when the step's optimal level > character level; 12 -> 5; 0/10 -> 6. Quest effort = 1 + sum.
- Tiers among doable quests by score: top 10 % tier 3, next 15 % tier 2, next 25 % tier 1 (at least one quest in tier 3 when any is doable).
- Gains follow D218 (`QuestRewards.For`), Bonus Pack included; unknown data hides gain and mark.
- Every user-visible string in the three `Strings*.resx`; no em dash anywhere; new `.cs` UTF-8 with BOM; `/usr/bin/git`, add files by name.

## Review Focus

- A character with no tracker link: no marks, no entry rows, gains still shown from the local level (Task 4 test on the pure ranking with null progress).
- Bulk data not yet read or refused: lists show no gains and no marks, nothing throws (Task 1 and Task 4).
- A series whose next quest is not doable: the series lists only its doable-in-order quests and stops at the first that is not (Task 2 test).
- Level box changed while the bulk read runs: marks recomputed once the data lands (Task 4, read the VM).
- XP/kamas switch: kamas ranking ignores quests with no kamas instead of putting them last with score 0 (Task 2 test).

---

### Task 1: Bulk quest facts in the names cache

**Files:** Modify `src/DtHub.Core/Papycha/TouchNames.cs`, `src/DtHub.Core/Papycha/TouchNameService.cs`; Test `tests/DtHub.Tests/Papycha/TouchNameServiceTests.cs`.

**Interfaces (produces):**
- `public sealed record TouchObjective(int TypeId, int[] Parameters, int StepOptimalLevel);`
- `public sealed record TouchQuestFacts(TouchStepReward?[] Steps, TouchObjective[] Objectives);` (steps in game order; a null step = missing formula fields)
- `TouchLanguageNames.Facts : Dictionary<int, TouchQuestFacts>` (by Papycha quest id) and `FactsReadUtc : DateTimeOffset`.
- `TouchNameService.FactsAsync(string language, CancellationToken ct = default) : Task<IReadOnlyDictionary<int, TouchQuestFacts>>` - serves the cache when fresh (7 days), else reads: `QuestSteps` for every step of every paired game quest (first game quest of each distinct name, as `StepsAsync` does), wait 60 s, `QuestObjectives` for their objective ids, wait 60 s, `QuestStepRewards` for their reward ids; builds rewards exactly as `ReadStepsAsync` does (reuse its helpers, do not duplicate the parsing); never throws except on cancellation; on failure keeps and returns what the cache had, `LastFailure` set.
- The 60 s pause is injectable for tests (`public TimeSpan FactsPause { get; init; } = TimeSpan.FromSeconds(60);`).

- [ ] Step 1: failing tests with `FakeDofusTouchClient.Rows`: one read issues exactly the three classes in order with the expected ids; a second call within 7 days asks nothing; a failing `QuestObjectives` read returns the previous cache and sets `LastFailure`; a step without `optimalLevel` gives a null entry in `Steps`.
- [ ] Step 2: run, see them fail. Step 3: implement. Step 4: focused tests then full suite. Step 5: commit `feat: read every paired quest's rewards and objectives once a week`.

### Task 2: Effort, doability, scores, tiers and the ranked entry (pure Core)

**Files:** Create `src/DtHub.Core/Papycha/QuestProfit.cs`; Test `tests/DtHub.Tests/Papycha/QuestProfitTests.cs`.

**Interfaces (produces):**
- `public enum RewardRanking { Xp, Kamas }`
- `public sealed record QuestEffort(double Value, IReadOnlyList<string> Reasons)`; `QuestProfit.EffortOf(TouchQuestFacts facts, int level) : QuestEffort` (reasons are short resource-backed phrases: "1 boss", "2 objets", "3 combats", "dialogues", "objectif vague"; keys `ProfitReason*`).
- `public sealed record QuestGain(long Xp, long Kamas)`; `QuestProfit.GainOf(TouchQuestFacts facts, int level, int bonusPercent) : QuestGain?` via `QuestRewards.For` (null when any step is null).
- `public static bool IsDoable(QuestSummary quest, TrackerProgress? progress, TouchQuestFacts facts, int level)` per Global Constraints (null progress -> false).
- `public sealed record QuestScore(int QuestId, QuestGain Gain, QuestEffort Effort, double Score, int Tier)`.
- `public static IReadOnlyDictionary<int, QuestScore> Score(IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, TouchQuestFacts> facts, TrackerProgress? progress, int level, int bonusPercent, RewardRanking ranking)` - doable quests only; score = gain (XP or kamas) / effort; kamas ranking skips quests with 0 kamas; tiers per Global Constraints.
- `public sealed record ProfitEntry(string Title, IReadOnlyList<QuestSummary> Quests, QuestGain Gain, QuestEffort Effort, int Tier, bool Started)` - `Quests.Count > 1` for a series.
- `public static IReadOnlyList<ProfitEntry> Entries(IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress? progress)` - series = quests sharing `SuccessName`, in `PlayOrder`; a series entry holds its doable quests from the first doable one, in order, stopping at the first quest that is neither doable nor done; `Started` when one of the series is done or in progress; started series first, then by summed score; single quests (no series, or a series of one doable quest) by score.

- [ ] Step 1: failing tests, invented data: effort of each objective type and the boss/over-level rules; doable rules one by one (completed, blocked, ignored, not available, level too high, repeatable); score and tiers on 20 quests (2 tier 3, 3 tier 2, 5 tier 1); kamas ranking skips 0-kamas quests; series cut at the first non-doable quest; started series first. Step 2-5 as usual, commit `feat: rank doable quests and series by gain for effort`.

### Task 3: The ranking setting

**Files:** `src/DtHub.Core/Settings/AppSettingsDocument.cs`, `src/DtHub.Core/Settings/SettingsService.cs`, `tests/DtHub.Tests/Settings/SettingsServiceTests.cs`.

**Produces:** `RewardRanking RewardRanking` (default Xp) and `SettingsService.SetRewardRankingAsync(RewardRanking ranking, CancellationToken ct = default)`, same shape as `SetRewardLevelAsync`. TDD as in Task 4 of the previous plan. Commit `feat: remember the XP or kamas ranking`.

### Task 4: Gains, marks, the entry and the boxes in the window

**Files:** `src/DtHub.App/ViewModels/QuestViewModel.cs` (`Marked`, the root section listing ~874, a new section like `QuestTree.NextSection`), `src/DtHub.App/ViewModels/QuestTrackerViewModel.cs` (ranking switch), `src/DtHub.Core/Papycha/QuestNode.cs` (new optional members `string? Gain = null`, `int Tier = 0`, `string? ProfitTip = null`), `src/DtHub.Core/Papycha/QuestTree.cs` (`public const int ProfitSection = -106;`), `src/DtHub.App/Windows/QuestWindow.xaml`, resx x3. Test: `tests/DtHub.Tests/Papycha/` for any new Core helper (compact gain text "12,7 M" / "86 k" / "950", tooltip text).

- On window open, start `FactsAsync` in the background; when it lands, and whenever level, bonus, ranking or progress change, recompute `QuestProfit.Score` once and rebuild the shown nodes.
- `Marked` adds `Gain` (compact text of XP or kamas per the ranking, from `GainOf` at the current level/bonus, also for non-doable quests) and `Tier`/`ProfitTip` from the score map (doable only).
- XAML: gain in a fixed right-hand column (width for "999,9 M"), priority glyph (three drawings, priority brush) in a fixed column before the status mark; both Hidden on quest rows without data, Collapsed on non-quest rows (key on the `Quest` property, not `Kind`: dungeons and paths are Kind Quest too); tooltips.
- Root list: a "Quêtes rentables" branch right after "Quêtes suivantes" when linked; its section lists `QuestProfit.Entries` (a series row shows its title, "n quêtes", summed gain and its tier; clicking a series opens its first doable quest; a single quest row opens the quest).
- Level and Bonus Pack boxes and an XP/kamas switch (two small toggle buttons) visible in every view of the window, not only while a guide is shown.
- Strings (three languages): the entry title, "n quêtes", the switch labels, the reason phrases, tooltip formats.
- Steps: Core helper tests first; then VM/XAML; build; full suite; commit `feat: gains, priority marks and profitable quests in the guides`.

### Task 5: Records

D219 in `docs/DECISIONS.md` (French, like D218): the doable rule, the effort model, tiers, the bulk read and its pauses, XP/kamas. `THIRD-PARTY-NOTICES.md`: objectives and step rewards of every paired quest read the same way. Changelogs (three languages, Unreleased): gains in the lists, priority marks, "Quêtes rentables", boxes everywhere. Build, full suite, commit `docs: record profitable quests`.

# Quest rewards and quick level box Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a quest's rewards (XP, kamas, items, other kinds counted) for the chosen character's level in the guides window, and let the player change that level in one box that saves to Papycha.

**Architecture:** A pure calculator in Core turns cached game steps into a reward total. `TouchNameService` already reads a quest's `QuestSteps` on demand; it now keeps their reward fields and reads `QuestStepRewards` and the reward items' names. The tracker bar gets a level box (Papycha character level when linked, a local setting otherwise) and an XP bonus box; the guide header shows one line.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xunit. Build and test through `rtk proxy dotnet … > log` as `CLAUDE.md` says.

**Spec:** `docs/superpowers/specs/2026-10-23-quest-rewards-and-level-design.md`

## Global Constraints

- Nothing read from the game is embedded in the repository (D212, `AGENTS.md`); tests use invented values, not copies of game rows, except the formula's three checked numbers.
- Requests to `dt-proxy-production-login.ankama-games.com` carry no account; at most `QuestStepRewards` and `Items` are added to a guide's step read; nothing else new.
- A missing field, an unpaired quest or an unreachable server hides the reward line: never a guessed number.
- Papycha's character save resends name, gender, class and colour exactly as read; fallbacks only as Papycha's own `app.js` does: gender `male`, class `cra`, colour `#6b992f`.
- Level is 1 to 200; bonus is 0 to 1000 percent.
- Every user-visible string in `Strings.resx`, `Strings.fr.resx`, `Strings.es.resx`.
- New `.cs` files: UTF-8 with BOM (`.editorconfig`).
- Never `git add -A`: another session may share the repository; add files by name.

## Review Focus

- A guide opened, then another before its rewards arrive: the first quest's line must never show on the second (Task 6 test).
- A cache written before this change (steps without rewards): rewards must still appear, by one re-read (Task 2 test).
- A level box typed `0`, `abc`, `250` or emptied: the old level stays, nothing is sent (Task 5 test).
- A Papycha save refused or a renewed phrase: the box goes back to the old level and the bar shows the usual notice (Task 5 test).
- A reward bracket outside the level (`levelMin`/`levelMax`), and `-1` bounds: only matching rows count (Task 1 test).

---

### Task 1: The reward calculator

**Files:**
- Modify: `src/DtHub.Core/Papycha/TouchNames.cs` (records below)
- Create: `src/DtHub.Core/Papycha/QuestRewards.cs`
- Test: `tests/DtHub.Tests/Papycha/QuestRewardsTests.cs`

**Interfaces:**
- Produces, in `TouchNames.cs`:
  - `public sealed record TouchStepReward(int OptimalLevel, float Duration, float XpRatio, float KamasRatio, bool KamasScaleWithPlayerLevel, TouchRewardRow[] Rows);`
  - `public sealed record TouchRewardRow(int LevelMin, int LevelMax, TouchItemReward[] Items, int Emotes, int Spells, int Titles, int Ornaments, int Companions, int Jobs);` (`-1` = no bound)
  - `public sealed record TouchItemReward(int ItemId, int Quantity);`
  - `TouchStep` gains a last parameter `TouchStepReward? Reward = null`.
- Produces, in `QuestRewards.cs`:
  - `public sealed record QuestRewardTotal(long Experience, long Kamas, IReadOnlyList<TouchItemReward> Items, int Emotes, int Spells, int Titles, int Ornaments, int Companions, int Jobs);`
  - `public static class QuestRewards { public static QuestRewardTotal? For(IReadOnlyList<TouchStep> steps, int level, int bonusPercent); public static long Experience(TouchStepReward step, int level, int bonusPercent); public static long Kamas(TouchStepReward step, int level); }`

- [ ] **Step 1: Write the failing tests** (French test names, like `TouchNameServiceTests`)

```csharp
[Fact] public void Le_Cra_191_sur_Gare_au_Kailleu_gagne_ce_que_le_jeu_affiche()
{
    var step = new TouchStepReward(160, 0.5f, 0.80000001f, 0.80000001f, false, []);
    Assert.Equal(1_976_445, QuestRewards.Experience(step, 191, 150));
    Assert.Equal(11_512, QuestRewards.Kamas(step, 191));
}
[Fact] public void L_Eni_184_sur_Distribution_de_Marrons_gagne_ce_que_le_jeu_affiche()
    => Assert.Equal(5_541_305, QuestRewards.Experience(new(200, 1f, 1.1f, 0f, true, []), 184, 150));
[Fact] public void Le_cas_plafonne_suit_la_regle_de_DOFUS_PC_en_attendant_le_jeu()
{
    // Game shows 1 162 075 for the Enu 190 on "Ça rend aimable." (step 2039); open, see Task 8.
    var step = new TouchStepReward(60, 1f, 0.80000001f, 1f, false, []);
    Assert.Equal(581_040, QuestRewards.Experience(step, 190, 150));
    Assert.Equal(4_780, QuestRewards.Kamas(step, 190));
}
[Fact] public void Les_kamas_suivent_le_niveau_du_joueur_quand_l_etape_le_dit()
    => Assert.Equal(43_980, QuestRewards.Kamas(new(100, 1f, 1f, 1f, true, []), 200)); // 200²+4000−20
[Fact] public void Seules_les_lignes_dont_les_bornes_contiennent_le_niveau_comptent()
{
    TouchRewardRow Row(int min, int max, int item) => new(min, max, [new(item, 1)], 0, 0, 0, 0, 0, 0);
    var step = new TouchStepReward(50, 1f, 1f, 0f, false, [Row(-1, -1, 1), Row(1, 99, 2), Row(100, 200, 3)]);
    var total = QuestRewards.For([new TouchStep("", "", [], step)], 150, 0)!;
    Assert.Equal([1, 3], total.Items.Select(i => i.ItemId));
}
[Fact] public void Une_quete_additionne_ses_etapes_et_regroupe_les_objets()
{
    var a = new TouchStepReward(50, 1f, 1f, 1f, false, [new(-1, -1, [new(7, 2)], 1, 0, 0, 0, 0, 0)]);
    var b = new TouchStepReward(50, 1f, 1f, 1f, false, [new(-1, -1, [new(7, 3)], 0, 1, 0, 0, 0, 0)]);
    var total = QuestRewards.For([new("", "", [], a), new("", "", [], b)], 40, 0)!;
    Assert.Equal(2 * QuestRewards.Experience(a, 40, 0), total.Experience);
    Assert.Equal([new TouchItemReward(7, 5)], total.Items);
    Assert.Equal((1, 1), (total.Emotes, total.Spells));
}
[Fact] public void Une_etape_sans_recompense_connue_ne_donne_aucun_total()
    => Assert.Null(QuestRewards.For([new TouchStep("", "", [], null)], 100, 0));
[Fact] public void Aucun_total_sans_niveau_ou_sans_etape()
{
    Assert.Null(QuestRewards.For([], 100, 0));
    Assert.Null(QuestRewards.For([new TouchStep("", "", [], new(50, 1f, 1f, 1f, false, []))], 0, 0));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `rtk proxy dotnet test tests/DtHub.Tests --filter QuestRewardsTests > /tmp/test.log 2>&1; grep -iE "erreur|error|échec|réussi|failed|passed|total" /tmp/test.log | tail -8`
Expected: build errors (types missing).

- [ ] **Step 3: Implement the records and `QuestRewards`**

The formula, exactly (spec, "The formula"); `d`, the ratios and `0.7f` are `float` widened to `double`, the rest `double`:

```csharp
static double Fixe(double x, TouchStepReward s) => Math.Floor(x * Math.Pow(100 + 2 * x, 2) / 20) * (double)s.Duration * (double)s.XpRatio;
// L ≤ opt: floor(Fixe(L) · (1 + b/100))
// L > opt: floor(floor((1 − 0.7f) · Fixe(opt) + 0.7f · Fixe(Math.Min(L, 1.5 · opt))) · (1 + b/100))
// the cap stays a double (61 · 1.5 = 91.5), as the game's Math.min on Numbers
// kamas: floor((K² + 20K − 20) · kamasRatio · d), K = scale ? L : opt
```

`For` returns null when `steps` is empty, `level < 1`, or any step's `Reward` is null; rows apply when `(LevelMin < 0 || level >= LevelMin) && (LevelMax < 0 || level <= LevelMax)`; items merge by id in first-seen order.

- [ ] **Step 4: Run the tests to see them pass** (same command; Expected: all `QuestRewardsTests` passed).

- [ ] **Step 5: Commit** `src/DtHub.Core/Papycha/TouchNames.cs`, `src/DtHub.Core/Papycha/QuestRewards.cs`, the test file: `feat: compute a quest's rewards for a level`.

---

### Task 2: Reading the rewards with the steps

**Files:**
- Modify: `src/DtHub.Core/Papycha/TouchNameService.cs` (`StepsAsync`, `ReadStepsAsync`)
- Test: `tests/DtHub.Tests/Papycha/TouchNameServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 records.
- Produces: `StepsAsync(papychaQuestId, language)` returns steps whose `Reward` is set when the server gave the fields; reward item names land in `TouchLanguageNames.Items` (the same map as dungeon keys).

- [ ] **Step 1: Write the failing tests**, feeding `_client.Rows["QuestSteps"]` with a step `{"id":11,"nameId":"e","descriptionId":"d","objectiveIds":[],"optimalLevel":60,"duration":1,"xpRatio":0.8,"kamasRatio":1,"kamasScaleWithPlayerLevel":false,"rewardsIds":[500]}` and `_client.Rows["QuestStepRewards"]` with `{"id":500,"stepId":11,"levelMin":-1,"levelMax":-1,"itemsReward":[[361,2]],"emotesReward":[1],"jobsReward":[],"spellsReward":[],"titlesReward":[],"ornamentsReward":[],"companionsReward":[]}`:
  - `Les_etapes_gardent_leur_recompense_et_le_nom_des_objets`: after `RefreshAsync("fr")` then `StepsAsync(2, "fr")` (Papycha 2 → game quest 11 in `FakeTable`, whose generated row has `stepIds:[11]`), the step's `Reward` equals `new TouchStepReward(60, 1f, 0.8f, 1f, false, [new(-1, -1, [new(361, 2)], 1, 0, 0, 0, 0, 0)])` (compare fields; arrays by sequence), `_client.Requested["QuestStepRewards"].Single()` is `[500]`, `_client.Requested["Items"]` last call contains `361`, and `CachedAsync("fr")!.Items[361]` is `"nom 361 fr"`.
  - `Une_etape_sans_champ_de_recompense_reste_sans_recompense`: step row without `optimalLevel` → `Reward` is null and no `QuestStepRewards` request.
  - `Un_cache_d_avant_les_recompenses_relit_les_etapes_une_fois`: pre-save a document whose `Steps[2]` holds one `TouchStep` with `Reward = null`; `StepsAsync(2, "fr")` asks `QuestSteps` again; a second call asks nothing.
- [ ] **Step 2: Run** (filter `TouchNameServiceTests`); Expected: the three new tests fail.
- [ ] **Step 3: Implement.** In `ReadStepsAsync`, read the reward fields with the existing `Int`/`Ints` helpers plus a `float` reader (`GetSingle`) and a bool reader; `Reward` is null unless `optimalLevel`, `duration`, `xpRatio` and `kamasRatio` are all present. One `GetAsync("QuestStepRewards", all rewardsIds)`; `itemsReward` pairs `[id, qty]`; the other six lists are counted. Then one `GetAsync("Items", reward item ids)` whose names go into `known.Items`. In `StepsAsync`, a cached entry where some step has `Reward is null` and was read before this change counts as missing: treat `cached.Any(s => s.Reward is null)` as a miss only once per quest per process (keep a `HashSet<int>` of re-read quests so a quest truly without rewards is not re-read on every open).
- [ ] **Step 4: Run** all `TouchNameServiceTests`; Expected: all pass, old ones included.
- [ ] **Step 5: Commit** `feat: read quest step rewards with the official steps`.

---

### Task 3: Papycha character save

**Files:**
- Modify: `src/DtHub.Core/Papycha/TrackerState.cs`, `src/DtHub.Core/Papycha/TrackerPageParser.cs`, `src/DtHub.Core/Papycha/IPapychaTracker.cs`, `src/DtHub.Infrastructure/Papycha/PapychaTracker.cs`, `tests/DtHub.Tests/Fakes/` (the fake tracker, if one implements `IPapychaTracker`)
- Test: `tests/DtHub.Tests/Papycha/TrackerPageParserTests.cs`, `tests/DtHub.Tests/Papycha/PapychaTrackerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TrackerCharacter(string Id, string Name, int Level, string Gender = "male", string ClassKey = "cra", string Color = "#6b992f");`
  - `IPapychaTracker.SaveCharacterAsync(string cookie, TrackerCharacter character, CancellationToken cancellationToken = default) : Task<TrackerWriteResult>`
  - `PapychaTracker.CharacterForm(string nonce, TrackerCharacter c)` → fields in this order: `action=papycha_tracker_character`, `nonce`, `mode=save`, `character_id`, `name`, `gender`, `level`, `class_key`, `color`.

- [ ] **Step 1: Failing tests:**
  - parser: a page whose character has `"gender":"female","class_key":"enutrof","color":"#123456"` gives those values; one without them gives `male`, `cra`, `#6b992f`.
  - tracker: `Save_character_posts_every_field_with_the_nonce` (StubHandler: page then `{"success":true}`; assert the posted form equals `CharacterForm` for the read nonce); `Save_character_refused_retries_once_after_a_fresh_read` (same retry rule as `SetStatusAsync`).
- [ ] **Step 2: Run** (filters `TrackerPageParserTests|PapychaTrackerTests`); Expected: fail.
- [ ] **Step 3: Implement.** The parser reads the three strings with its `Text` helper and the fallbacks above. `SaveCharacterAsync` shares `SetStatusAsync`'s read-first, post, one-retry flow: extract the flow into one private method taking the form, so both writes keep the same nonce handling.
- [ ] **Step 4: Run** the two test classes plus `TrackerCharacterMessageTests`; Expected: pass.
- [ ] **Step 5: Commit** `feat: save a character's level on Papycha`.

---

### Task 4: Local level and XP bonus settings

**Files:**
- Modify: `src/DtHub.Core/Settings/AppSettingsDocument.cs` (near `PapychaCharacterId`), `src/DtHub.Core/Settings/SettingsService.cs` (near `SetPapychaCharacterAsync`)
- Test: the existing settings test class that covers `SetPapychaCharacterAsync` (find it with `ct refs SetPapychaCharacterAsync` or grep in `tests/`)

**Interfaces:**
- Produces: `int RewardLevel` (0 = unknown), `int RewardXpBonus` (percent, 0) in the document; `SettingsService.SetRewardLevelAsync(int level, CancellationToken ct = default)` and `SetRewardXpBonusAsync(int percent, CancellationToken ct = default)`, both no-ops when unchanged, same shape as `SetPapychaCharacterAsync`.

- [ ] **Step 1: Failing tests:** each setter stores its value and survives a reload; an unchanged value does not save (mirror the existing `SetPapychaCharacterAsync` tests).
- [ ] **Step 2: Run** that test class; Expected: fail.
- [ ] **Step 3: Implement** the two properties and setters.
- [ ] **Step 4: Run**; Expected: pass.
- [ ] **Step 5: Commit** `feat: remember a reward level and an XP bonus`.

---

### Task 5: The level and bonus boxes in the tracker bar

**Files:**
- Modify: `src/DtHub.App/ViewModels/QuestTrackerViewModel.cs`, `src/DtHub.App/Windows/QuestWindow.xaml` (tracker bar, the `Grid` holding the status controls, near line 162), `src/DtHub.App/Windows/QuestWindow.xaml.cs` if handlers are needed, `src/DtHub.Core/Localization/Strings*.resx`
- Test: `tests/DtHub.Tests/` view-model tests for `QuestTrackerViewModel` (create `QuestTrackerLevelTests.cs` next to the existing tracker view-model tests if any, else under `tests/DtHub.Tests/Papycha/`)

**Interfaces:**
- Consumes: Task 3 `SaveCharacterAsync`, Task 4 settings.
- Produces on `QuestTrackerViewModel`:
  - `int RewardLevel { get; }` — `SelectedCharacter.Level` when linked, else the setting; observable.
  - `int RewardXpBonus { get; }` — the setting; observable.
  - `Task<bool> SetRewardLevelAsync(string text)` — parses 1..200; linked: saves the selected character with the new level through `SaveCharacterAsync`, replaces it in `Characters` and `SelectedCharacter` on success, else keeps the old one and sets `Notice` as other write failures do; not linked: stores the setting. Returns whether it changed.
  - `Task<bool> SetRewardXpBonusAsync(string text)` — parses 0..1000, stores the setting.

- [ ] **Step 1: Failing tests** with the fake tracker: `0`, `abc`, `250`, `""` leave `RewardLevel` unchanged and send nothing; `191` while linked sends one save with level 191 and every other field unchanged, then `RewardLevel` is 191; a refused save leaves 190 and a non-empty `Notice`; not linked, `191` stores the setting; bonus `150` stores 150, `-5` and `abc` are refused.
- [ ] **Step 2: Run**; Expected: fail.
- [ ] **Step 3: Implement**, then the XAML: a short `TextBox` showing `RewardLevel` with a label from `RewardLevelLabel` ("Niv." / "Lvl" / "Niv."), applying on Enter and on lost focus, and a second one for the bonus with `RewardXpBonusLabel` ("Bonus XP %" / "XP bonus %" / "Bono XP %"); visible whenever the window shows a quest, linked or not.
- [ ] **Step 4: Run** the new tests and the whole suite (`rtk proxy dotnet test > /tmp/test.log 2>&1`); Expected: all pass.
- [ ] **Step 5: Commit** `feat: change the character's level from the guides window`.

---

### Task 6: The reward line under the quest title

**Files:**
- Modify: `src/DtHub.App/ViewModels/QuestViewModel.cs` (quest shown, near line 1170; `ResetOfficialSteps`), `src/DtHub.App/Windows/QuestWindow.xaml` (header `StackPanel`, under `QuestTitle`, near line 343), `src/DtHub.Core/Localization/Strings*.resx`
- Create: `src/DtHub.Core/Papycha/QuestRewardText.cs`
- Test: `tests/DtHub.Tests/Papycha/QuestRewardTextTests.cs`, and a view-model test for the stale-quest guard

**Interfaces:**
- Consumes: Task 1 `QuestRewards.For`, Task 2 `StepsAsync`, Task 5 `RewardLevel`/`RewardXpBonus`.
- Produces: `QuestRewardText.Format(QuestRewardTotal total, int level, int bonusPercent, IReadOnlyDictionary<int, string> itemNames) : string`; `QuestViewModel.RewardText` (string, empty hides the line).

- [ ] **Step 1: Failing tests:**
  - `Format` in French gives `Niv. 191 · bonus +150 % : 1 976 445 XP · 11 512 kamas · 10 × A, 1 × B` for items 1 (10, "A") and 2 (1, "B"); bonus 0 drops `· bonus +0 %`; zero kamas drops the kamas part; other kinds append `1 sort, 1 titre` with singular/plural from resources; an item without a name is left out.
  - view model: show quest A, then quest B before A's steps arrive; when A's steps complete, `RewardText` stays empty (B is shown).
- [ ] **Step 2: Run**; Expected: fail.
- [ ] **Step 3: Implement.** When a quest is shown and `RewardLevel > 0`, start `StepsAsync(quest.Id, TouchNameService.CurrentLanguage)` without awaiting the caller, guard with `_current?.Id == quest.Id` on completion (as `ToggleOfficialStepsAsync` does), and reuse the result for the official steps view so a guide reads its steps once. Recompute `RewardText` when `RewardLevel` or `RewardXpBonus` change. Numbers use the current culture's group separator; all words from resources (`RewardLine`, `RewardBonus`, `RewardKamas`, `RewardSpell`/`RewardSpells`, etc.).
- [ ] **Step 4: Run** the new tests and the whole suite; Expected: all pass.
- [ ] **Step 5: Commit** `feat: show a quest's rewards under its title`.

---

### Task 7: Records

**Files:**
- Modify: `docs/DECISIONS.md` (next free number after D217), `THIRD-PARTY-NOTICES.md` (the game data section D212 added), `CHANGELOG.md`, `CHANGELOG.fr.md`, `CHANGELOG.es.md` (an `## [Unreleased]` section at the top if none)

- [ ] **Step 1:** Decision: rewards read at runtime with the steps, formula and its three checked cases, the open capped case, level box saving to Papycha, bonus manual. Notices: quest rewards read like names, nothing embedded. Changelog, one entry per language: rewards under the quest title for the character's level; level box that saves to Papycha.
- [ ] **Step 2:** `rtk proxy dotnet build > /tmp/build.log 2>&1` then the full test run; Expected: build succeeded, all tests passed.
- [ ] **Step 3: Commit** `docs: record quest rewards and the level box`.

---

### Task 8: Settle the capped case in game (with the user)

- [ ] **Step 1:** With DT Hub running and a character's quest journal open, select a quest whose level is below two thirds of the character's; capture the window (`adb exec-out screencap -d <SurfaceFlinger id>`, see `C:\Dev\dthub-import-quetes`) and note the character's exact level and bonus.
- [ ] **Step 2:** Read that quest's steps once (`QuestSteps` by the step ids in the names cache), compute with `QuestRewards.Experience`, compare.
- [ ] **Step 3:** If the game differs, find the rule that matches both capped cases (this one and Enu/2039), change `QuestRewards` and the capped test's expected value to the game's, run the suite, commit `fix: match the game on capped quest experience`. If it matches, the Enu case had a bonus we cannot see: note it in the decision.
- [ ] **Step 4:** Only then is the branch ready to merge.

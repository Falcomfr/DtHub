# Profitable quests: rewards in every list, priority marks, a ranked entry

Date: 2026-10-23. Status: asked by the user in conversation; the choices marked as
Claude's are open to correction. Builds on D218 (quest rewards for a level).

## Why

The user wants to know, for the chosen character, which quests are worth doing now: see a
quest's reward in every list (not only in the open guide), spot the best ones at a glance
with a small priority mark, and get a ranked list. In their words: take quest chains into
account ("c'est rentable de faire cette suite"), never propose a quest whose earlier quests
are not done, and propose the next quests of chains. Ranking by XP and by kamas, both
("tu peux faire xp et kamas, les 2 propositions"). The level and Bonus Pack boxes must be
usable from the lists too, not only in a quest's guide.

## Goals

1. **Rewards in lists.** Every quest row shows the quest's gain for the chosen character
   in a fixed right-hand column, compact ("12,7 M"), XP or kamas following the chosen
   ranking; the tooltip gives XP, kamas and items. Same formula and data as D218.
2. **Level and Bonus Pack boxes everywhere** in the guides window, list views included;
   changing them refreshes every gain and mark at once.
3. **Priority marks.** On every quest row of every list, a small mark in three tiers for
   quests doable now, ranked among the character's doable quests; tooltip explains the
   tier (gain, effort reasons, series length).
4. **"Quêtes rentables" entry** at the top of the quest areas, next to "Quêtes suivantes":
   the doable quests and series ranked by gain for effort, series in progress first.
5. **XP / kamas switch** in that entry and for the marks and the list column; remembered.

## Non-goals

- Reading the character's progress inside a quest (current step, objectives done): only
  the game shows it; not read.
- Repeatable, daily, weekly and seasonal quests in the ranking (Claude's choice: their
  availability depends on a cooldown or an event DT Hub cannot see).
- Monster data (too heavy to download for every quest, about 30 KB per monster).

## Anti-goals

- Proposing a quest the character cannot start: not done, not blocked or ignored on the
  tracker, in Papycha's `available_quests` for that character or in progress, and its
  first step's optimal level at most the character's level (Claude's choice).
- A wrong gain: same rule as D218, unknown data hides the gain and the mark.
- Hammering the game's server: the steps, objectives and rewards of every paired quest are
  read once, in the background, in at most three requests a minute apart (about 2.5 MB in
  all), and kept with the names for 7 days.

## Data

Already in DT Hub: the catalog (series by `SuccessName`, play order `PlayOrder`, recurrence),
the id table (Papycha quest to game quests), the tracker's statuses and `Available` set per
character, the reward formula (`QuestRewards`).

New: a bulk read of `QuestSteps` (reward fields and `objectiveIds`), `QuestObjectives`
(`typeId`, `parameters`) and `QuestStepRewards` for every paired quest, cached per language
like the names. Item names stay read on demand (tooltip, open guide).

## Effort model (Claude's choice, tunable)

Per objective, by `typeId` (templates from `QuestObjectiveTypes`, read 2026-10-23):

| Objective | Types | Effort |
| --- | --- | --- |
| Go to, return to, show | 1, 9, 2 | 1 |
| Discover a map | 4 | 1.5 |
| Bring items (x quantity) | 3 | 2 + quantity / 10, at most 7 |
| Craft | 20 | 3 |
| Defeat monsters | 6, 14 | 3; 8 when "1 in one fight" (a boss in practice); +4 when the step's optimal level is above the character's |
| Bring souls | 12 | 5 |
| Free text (dungeon runs, help a village, empty the keyring) | 0, 10 | 6 |

A quest's effort is 1 + the sum of its objectives. Its score is its gain (XP or kamas at the
character's level, Bonus Pack included) divided by its effort. A series' score sums the gains
and efforts of its remaining doable-in-order quests.

## Tiers and the entry

Among the character's doable quests, ranked by score: top 10 % tier 3, next 15 % tier 2,
next 25 % tier 1, the rest no mark (Claude's choice). Marks: a filled flame, a half flame, a
small dot (glyphs in the list's style, priority brush). The entry lists series first (those
with a quest done or in progress), then single quests, each with gain, effort reasons in a
few words and its tier mark; clicking opens the guide as any row does.

## Left to the builder

Class names, the bulk cache format, the exact glyphs and brush, the entry's row layout,
where the switch sits, decision number, changelog wording in three languages, order of work.

# Profitable quests: rewards in every list, priority marks, a ranked entry

Date: 2026-10-23. Status: asked by the user in conversation; the choices marked as
Claude's are open to correction. Builds on D218 (quest rewards for a level).

## Why

The user wants to know, for the chosen character, which quests are worth doing now: see a
quest's reward in every list (not only in the open guide), spot the best ones at a glance
with a small priority mark, and get a ranked list. In their words: take quest chains into
account ("c'est rentable de faire cette suite"), never propose a quest whose earlier quests
are not done, and propose the next quests of chains. XP and kamas together, no switch
("donne les 2 direct xp kamas, ça sert à rien de différencier, moi ce que je veux c'est que
l'utilisateur sache ce qui est recommandé et rentable à faire en prio, que ce soit pour xp ou
kamas"). The level and Bonus Pack boxes must be
usable from the lists too, not only in a quest's guide.

## Goals

1. **Rewards in lists.** Every quest row shows the quest's gains for the chosen character
   in two fixed right-hand columns, compact ("12,7 M"): XP in blue, then kamas in gold
   (empty when none); the tooltip gives XP and kamas. Same formula and data as D218.
2. **Level and Bonus Pack boxes everywhere** in the guides window, list views included;
   changing them refreshes every gain and mark at once.
3. **Priority marks.** On every quest row of every list, a small mark in three tiers for
   quests doable now, ranked among the character's doable quests; tooltip explains the
   tier, whether the quest is worth it mostly for XP or for kamas, and the effort reasons.
4. **"Recommandées" view**, opened by a button in the window's top bar next to "À faire"
   and "Accessibles" (its only entry point): two short lists, "Pour l'XP" and "Pour les kamas", 8 rows at most
   each, every side ranked on its own gain per minute (ties by catalog order, then quest id), a row possibly in both;
   single quests only, a series by its next doable quest with "· Series 3/11" after its zone (D219). Rows carry no priority mark; the combined score below only drives the marks of
   the other lists (D219). Pressed again, it returns to where the reader was, guide included; Back
   returns to the section left, with its list open.

## Non-goals

- Reading the character's progress inside a quest (current step, objectives done): only
  the game shows it; not read.
- Repeatable, daily, weekly and seasonal quests in the ranking (Claude's choice: their
  availability depends on a cooldown or an event DT Hub cannot see).
- Monster data (too heavy to download for every quest, about 30 KB per monster).

## Anti-goals

- Proposing a quest the character cannot start: not done, not blocked or ignored on the
  tracker, in Papycha's `available_quests` for that character, in progress, or, when that list is empty, with every previous quest done
  (`available_quests` is empty for a character that is not the site's active one; for the active one the list is the
  truth), and its
  first step's optimal level at most the character's level (Claude's choice).
- A wrong gain: same rule as D218, unknown data hides the gain and the mark.
- Hammering the game's server: the steps, objectives and rewards of every paired quest are
  read once, in the background, in at most four requests a minute apart (about 2.5 MB in
  all), and kept with the names for 7 days.

## Data

Already in DT Hub: the catalog (series by `SuccessName`, play order `PlayOrder`, recurrence),
the id table (Papycha quest to game quests), the tracker's statuses and `Available` set per
character, the reward formula (`QuestRewards`).

New: a bulk read of `QuestSteps` (reward fields and `objectiveIds`), `QuestObjectives`
(`typeId`, `parameters`) and `QuestStepRewards` for every paired quest, cached per language
like the names. Item names stay read on demand (tooltip, open guide).

## Effort model (Claude's choice, tunable)

Superseded by an estimated time in minutes (D221): the ranking is gain per minute and the short lists drop what takes more
than 60 minutes. The first-version table follows, kept for the record.

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

A quest's effort is 1 + the sum of its objectives.

## Combined score

Now per minute (D221), and only used for the priority marks, not for the order of the short lists. The reason for that last
point (stability): each side is divided by the best doable ratio, so a quest finished between two displays rescaled the sides
against each other and reordered rows that had not changed. Each short list now ranks on its own side's gain per minute, which
depends on the row alone.

For each doable quest with a known gain (XP and kamas at the character's level, Bonus Pack
included), `xpRatio = XP / effort` and `kamasRatio = kamas / effort`. Each is divided by its
maximum among those quests (a maximum of 0 makes that side 0); the score is the larger of the
two, and the side that gave it is kept for the tooltip (XP on a tie). A quest giving neither XP
nor kamas is not scored. A series sums the gains and efforts of its remaining doable-in-order
quests and takes the same two ratios against the same quest maxima.

## Tiers and the entry

Among the character's doable quests, ranked by the combined score: top 10 % tier 3, next 15 % tier 2,
next 25 % tier 1, the rest no mark (Claude's choice). Marks: a filled flame, a half flame, a
small dot (glyphs in the list's style, priority brush). The view shows the two short lists of single
quests, each with both gains, its time and its reasons in a few words (no tier mark); clicking opens the guide as any row does. Not linked, it shows a muted
line asking to link the Papycha tracker.

## Left to the builder

Class names, the bulk cache format, the exact glyphs and brush, the entry's row layout,
decision number, changelog wording in three languages, order of work.

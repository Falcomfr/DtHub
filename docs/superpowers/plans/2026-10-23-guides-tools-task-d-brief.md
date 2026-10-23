# Task D brief: wire the guides tools into the window, with the user's latest requests

Worktree /mnt/c/Dev/DTHub-recompenses, branch feat/recompenses-quetes. Read first: D218, D219 (docs/DECISIONS.md), the specs
`docs/superpowers/specs/2026-10-23-profitable-quests-design.md` and `2026-10-23-guides-tools-design.md`, the plan
`docs/superpowers/plans/2026-10-23-guides-tools.md`. Merged Core you wire: `QuestProfit` (Score, Entries with `ProfitEntry.Score`,
`DungeonsOf`, `DungeonValues`), `QuestPlanning` (Team, ByArea, WholeLevel), `ItemSourceService` (SourceAsync; merged before you
start: read its report /tmp/claude-1000/-home-falcom-dev/4ff22716-33c0-45b2-aa85-654f4249f921/scratchpad/task-a-report.md).

The user's words since the plan (binding):
- "Recommandées change à chaque fois que je les affiche, c'est bizarre"
- "afficher les autres récompenses dans les listes, juste une icône au survol"
- "sois sûr que les quêtes ne soient pas ultra longues à faire, que ce soit vraiment cohérent"
- "check si c'est cohérent sans le papycha lié"
- "recommandées, ça doit être bien les quêtes faisables recommandées, je voulais soit kamas soit xp aussi recommandé, ne fais pas
  de listes trop longues"

## 1. Recommandées, redone (Core changes in QuestProfit allowed, TDD)

- Two short lists in the view, one after the other: "Pour l'XP" and "Pour les kamas" (en "For XP" / "For kamas", es "Para la XP"
  / "Para los kamas"), at most 8 entries each, doable entries only (strictly doable quests, and series runs starting at a strictly
  doable quest). An entry may appear in both. Each list is ordered by its own side's ratio (XP per effort, kamas per effort), not
  the combined score; dungeons (DungeonValue) join both lists on the same per-side ratio. The combined score keeps driving the
  priority marks in other lists.
- Never ultra long: fix the effort of item objectives (today `2 + quantity/10` capped at 7, so 365 pages cost 7): use
  `2 + quantity/10` capped at 40. Exclude from both lists any entry with effort above 25, any series run longer than 4 quests (cut
  the run to its first 4), and any quest with a free-text objective (types 0, 10) whose text matches the "whole dungeon list" kind
  ("trousseau", "keyring", "llavero") or with more than 5 soul objectives. Tests for each rule.
- Stable between displays: find out with evidence why the list changes each time it is shown (suspects: the tracker re-read on
  show changing progress, ties broken by enumeration order of a Dictionary/HashSet, the facts read landing, the level box
  notifying, Started flags) and fix the cause; ties broken by catalog order then quest id. Add a test that two calls with the same
  inputs give the same order, and describe the root cause in your report.
- Team toggle "Moi / Équipe" (QuestPlanning.Team): same two short lists, gains summed over characters, "n persos" on the row,
  names in the tooltip.
- Zone: instead of a grouping toggle, show the area of the entry's first quest as a muted suffix on the row ("· Frigost"), from
  the zone name QuestTree gives; keep `QuestPlanning.ByArea` unused only if it brings nothing then (say so; do not delete Core
  code another task may need without reason).
- Level line (QuestPlanning.WholeLevel): the game publishes no XP table (task C proved it); there is no table, so do not show the
  line and do not embed one (the user has not decided); leave the wiring ready behind an empty table.
- Not linked: the view shows only the existing "link Papycha" line; gains in the other lists still show from the local level,
  marks and recommendations do not (verify every path and describe it in the report).

## 2. Lists

- Other rewards: a small gift glyph on quest rows whose reward has items, emotes, spells, titles, ornaments, companions or jobs
  (from the cached facts at the character's level); its tooltip lists them (item names from the names cache when known, counts
  for the other kinds). Same compact, no-empty-slot rule as the other row icons.
- Dungeon rows: priority mark from DungeonValue, both gains with the existing icons, tooltip "Fait avancer n quête(s) : titres".
- Where to find (ItemSourceService): in the gain tooltip of a quest row with item objectives and under the reward line of an open
  guide: "Lâché par M (niv. L) · zones", "pendant la quête" for conditional droppers, "Récolte : Job niv. L", "Fabrication : Job
  niv. L", "Archimonstre : … (version normale : …)", "Source inconnue" (never "événement ou boutique" unless the data says so: it
  does not, so say "Source inconnue"); read on hover / guide open; never block the UI; respect the service's cooldown.
- "Suivantes": exclude repeatable quests from the computed next quests (they made the list very long).
- The tracker bar at the 440 px minimum width with a guide open still clips the character selector: make the right-hand group
  wrap under the left one when space runs out (or lower the ComboBox minimum), verify by reasoning about the widths.

## Rules

TDD for every Core change (RED/GREEN in the report). Build, full suite, `dotnet format --verify-no-changes` through
`rtk proxy dotnet ... > /tmp/d.log 2>&1` then grep the summary; never `dotnet.exe`, never trust exit codes. `/usr/bin/git`, add
files by name, conventional subjects, several commits fine, trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
No em dash (U+2014) anywhere (a test walks the worktree). Strings in Strings.resx (en), .fr, .es. New .cs UTF-8 with BOM. Never
touch build/publish or a running DT Hub. The test project does not reference DtHub.App: read VM/XAML carefully. Report to
/tmp/claude-1000/-home-falcom-dev/4ff22716-33c0-45b2-aa85-654f4249f921/scratchpad/task-d-report.md, including what each row
type and the Recommandées view look like after your change, and what you are unsure renders well.

# Brief: a dungeon is worth the quests it advances

Worktree /mnt/c/Dev/DTHub-recompenses, branch feat/recompenses-quetes. C#/WPF .NET 10. Read D219 and the profitable-quests
spec first (docs/DECISIONS.md, docs/superpowers/specs/2026-10-23-profitable-quests-design.md), then QuestProfit.cs and
TouchNameService.FactsAsync.

The user wants gains and recommendations for dungeons too. The game's data has no room composition and fight XP depends on
wisdom, team and random groups, so a dungeon's own XP/kamas cannot be computed exactly; the user accepted (2026-10-23): "a
dungeon is worth the quests it advances". Many profitable quests need a dungeon: "Tour de force" (Ougah, "Découvrir la carte
Sortie du Donjon Fungus"), "Terre à terre", "La voie du guerrier", "Donjons et trouffions" ("Braver les dangers de la Serre du
Royalmouth"...).

## Changes

1. **Objective texts in the facts read.** `TouchObjective` gains `string Text` (empty when none). The weekly facts read adds a
   fourth request after the objectives (same 60 s pause, same back-off and cache rules): `client.GetTextsAsync` for the text ids
   of objectives of types 0, 4 and 10 (their parameters[0] is a `data/text` id, see TouchObjectiveText.KindOf). Update the
   global rule "three requests" to "four" in D219, the spec and the notices. Old cached facts without texts are re-read once
   (as for rewards) so dungeon links appear.
2. **Matching (Core, pure, tested).** `QuestProfit.DungeonsOf(TouchQuestFacts facts, IReadOnlyDictionary<int, string> dungeonNames)`
   returns the game dungeon ids whose name appears in an objective's text, compared on a normalised form (lower case, accents
   and punctuation removed, leading articles "le", "la", "les", "l", "du", "des", "de" dropped on both sides, so "Sortie du
   Donjon Fungus" matches "Donjon Fungus" and "la Serre du Royalmouth" matches "Serre du Royalmouth"). A Papycha dungeon maps
   to game dungeons through the id table (`TouchIdTable.Dungeons`), names from the names cache (`TouchLanguageNames.Dungeons`).
3. **Dungeon value.** For the chosen character, a dungeon's value = the doable or in-progress quests (strictly doable, or
   series continuation members, i.e. the same quests "Recommandées" can list) that it advances: summed XP and kamas, summed
   effort, combined score with the same maxima as quests; tier on the same scale (a dungeon gets a tier only if its combined
   score would place it in the quests' tiers). Not linked or no quest: no value.
4. **Display.** Dungeon rows (zones, the dungeon lists, search) show the priority mark, both gains with icons, and in the
   tooltip "Fait avancer n quête(s) : titre, titre…" (three languages). The "Recommandées" view mixes dungeons with quests and
   series, sorted by the same combined score; a dungeon row opens the dungeon's guide.
5. **Docs**: D219 (or D220 if you prefer a separate decision: your call) states the rule and that a dungeon's own fight XP and
   drops are not computed; changelogs (three languages): dungeons show the quests they advance and their gains.

## Rules
TDD for Core (RED/GREEN in the report); build, full suite, `dotnet format --verify-no-changes`, read logs (`rtk proxy dotnet ...
> /tmp/x.log 2>&1` then grep); `/usr/bin/git`, add by name, `feat:` subject, trailer `Co-Authored-By: Claude Opus 5.5
<noreply@anthropic.com>`; no em dash anywhere; new .cs with BOM; strings in the three resx; never touch build/publish or a
running DT Hub; App is not referenced by tests. Report to
/tmp/claude-1000/-home-falcom-dev/4ff22716-33c0-45b2-aa85-654f4249f921/scratchpad/dungeon-report.md; reply with status,
commits, one-line test summary, concerns.

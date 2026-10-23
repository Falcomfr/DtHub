# Guides tools: dungeon value, team, where to find, itinerary, archimonsters, levels

Date: 2026-10-23. Status: asked by the user in conversation ("2, 3, 4, 5 oui, go", plus the level line); choices marked as
Claude's are open to correction. Builds on D218 (rewards) and D219 (profitable quests and the "Recommandées" view).

## Why

The "Recommandées" view tells one character what to do next. The user plays four characters (two game windows, two characters
each) and wants more help deciding and doing: dungeons valued too, what is worth doing as a team, where to find the items a
quest asks for, which recommended quests sit close together, where the archimonsters some quests need live, and what the
recommended quests mean in levels.

## 1. Dungeon value (accepted by the user: "a dungeon is worth the quests it advances")

Fight XP and drops are not computable (no room composition in the game's data; XP depends on wisdom, team, random groups).
A dungeon's value = the recommended quests it advances (doable, in progress, or series continuation members), found by
matching objective texts ("Découvrir la carte Sortie du Donjon Fungus", "Braver les dangers de la Serre du Royalmouth") to
the game's dungeon names. The weekly facts read adds a fourth request: the `data/text` of objective types 0, 4, 10. Dungeon
rows show the priority mark, both gains and "Fait avancer n quêtes"; dungeons join "Recommandées", ranked the same way.
Only doable quests count, not the not-yet-startable rest of a series; a dungeon joins the short lists only when it advances at
least two of them. Detailed brief: the controller's dungeon brief (copied into the plan).

## 2. Team recommendations

Papycha's tracker already holds every character of the account with its level and progress. A "Moi" / "Équipe" toggle in
"Recommandées" matches series by name, takes each character's best run of a series at its own level (one Bonus Pack setting
for the account, Claude's choice), sums the gains, shows "3 persos" with their names in the tooltip, and lists only series
shared by at least two characters. No new data.

## 3. Where to find (items asked by quests)

For objectives "Ramener / Montrer x N <item>", the tooltip and the guide's reward area say how to get the item, from the game's
data, read on demand (hover or guide open) and cached: monsters that drop it (`Items.dropMonsterIds` -> `Monsters` names and
level, at most 5 shown), harvest (`Skills` whose gathered item is it: job and level), craft (`Recipes` producing it: job and
level), or "Source inconnue" when none applies (D220: DT Hub cannot tell an event or a shop from a gap in the data). Also read: `Items`, `Monsters`, `SubAreas`, `Skills`, `Recipes` and `Jobs` by ids, cached 30 days, 10 minutes of cooldown after a failure, 30 s read timeout, 400 ms hover delay, at least 2 s between two requests, 5 items at most per guide, a read once started finishes and is cached even if its hover closes; a drop that only exists in `conditionnalDropMonsterIds` reads "pendant la quête". Requests: a handful of ids per item, never a whole heavy class.

## 4. Itinerary by zone

Built as a grouping, then dropped: grouping by area would break the two per-side orders of "Recommandées" (D219). Each row
carries a muted zone suffix after its title instead (the first quest's section name). `QuestPlanning.ByArea`, written but never
used, was deleted. No new data.

## 5. Archimonsters

For items that are archimonster souls (the monster dropping them is an archimonster: `Monsters.isMiniBoss`), the "where to
find" tooltip names the archimonster, its normal counterpart (`correspondingMiniBossId` reversed) and the sub-areas where it
lives (`Monsters.subareas` -> `SubAreas` names, at most 5). Read on demand like 3.

## 6. Levels

Finding: the game's data server publishes no XP table. `Experiences` answers 400 and the 115 classes of the client's script
hold none (the client gets the level's XP from the game's messages). The line is wired (`QuestPlanning.WholeLevel`, fed an empty
table) and never shows. Embedding a table is the user's call and is not decided. The intended behaviour: in "Recommandées", a
line above the list:
"Ces N quêtes valent un niveau entier (191 → 192)" when the best-ranked entries' summed XP (for the chosen character, in the
shown order, smallest N) reaches the XP between the current level and the next; nothing at level 200. DT Hub does not know the
XP already earned inside the level, so the sentence promises a whole level, never "te font passer" a given threshold
(Claude's wording; an optional "% of the level" box can come later if the user wants it).

## Anti-goals

- No whole `Monsters` or `Items` class read (several MB): ids only, on demand, cached.
- No guessed number: unknown data hides the line or the hint.
- Nothing tied to the player's Ankama account.

## Left to the builder

Class and file names, cache layout, exact UI placement of the toggles, glyphs, decision numbering, changelog wording, order.

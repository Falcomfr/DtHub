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
Detailed brief: the controller's dungeon brief (copied into the plan).

## 2. Team recommendations

Papycha's tracker already holds every character of the account with its level and progress. A "En équipe" toggle in
"Recommandées" ranks entries by their gain summed over every character for which they are doable (each at its own level and
Bonus Pack setting, Claude's choice: one Bonus Pack setting for the account), shows "3 persos" with their names in the tooltip,
and lists only entries doable by at least two characters. No new data.

## 3. Where to find (items asked by quests)

For objectives "Ramener / Montrer x N <item>", the tooltip and the guide's reward area say how to get the item, from the game's
data, read on demand (hover or guide open) and cached: monsters that drop it (`Items.dropMonsterIds` -> `Monsters` names and
level, at most 5 shown), harvest (`Skills` whose gathered item is it: job and level), craft (`Recipes` producing it: job and
level), or "Événement ou boutique" when none applies. Requests: a handful of ids per item, never a whole heavy class.

## 4. Itinerary by zone

In "Recommandées", a "Par zone" grouping: entries grouped by the area of their first quest's start position (Papycha's
`StartPosition` and the quest's section), groups ordered by their summed score, rows inside by score; each group header gives
the area and the number of recommended entries. No new data.

## 5. Archimonsters

For items that are archimonster souls (the monster dropping them is an archimonster: `Monsters.isMiniBoss`), the "where to
find" tooltip names the archimonster, its normal counterpart (`correspondingMiniBossId` reversed) and the sub-areas where it
lives (`Monsters.subareas` -> `SubAreas` names, at most 5). Read on demand like 3.

## 6. Levels

The game's XP table (`Experiences`: level -> total XP) is read once with the facts. In "Recommandées", a line above the list:
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

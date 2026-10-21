# Guides in English and Spanish: names and steps from DOFUS Touch itself

Date: 2026-10-21. Status: approved in conversation, implementation follows.

## Why

DT Hub speaks English, French and Spanish, but the guides window lists
papycha.fr's catalog, which is French only. An English or Spanish player
sees French quest and dungeon names. The "Translate" button only sends the
open page to Google Translate in the browser; the list itself stays French.

What the user wants, in their words: the player must **find the quest in
their own game**. A translated name is only useful if it is exactly the name
DOFUS Touch shows in that language. A close translation is worse than the
French one, because it sends the player looking for a name that does not
exist. The user ruled out showing the French name next to the translation.

## Where the names come from

papycha.fr has no translation. DofusDB (api.dofusdb.fr) has official names,
but those of DOFUS on PC: only 513 of the 782 quest titles match, and its
English may differ from Touch's.

DOFUS Touch's own client reads its texts from the game's data server,
without an account:

    POST https://dt-proxy-production-login.ankama-games.com/data/map?lang=fr|en|es
    {"class": "Quests", "ids": [230]}          (omit ids for the whole class)

    POST .../data/text?lang=en   {"ids": [662825, ...]}   (free texts by id)

The host is in the APK's `res/xml/config.xml`; the class names are in the
`build/script.js` the same host serves. Measured on 2026-10-21 against the
embedded catalog (`assets/papycha-catalog.json`, 782 quests, 93 dungeons):

| | Found in the game | How |
| --- | --- | --- |
| Quests | 765 of 782 | 745 by exact French name, 20 paired by hand |
| Dungeons, raids, lairs | 86 of 93 | name, key, boss, or the two lairs of each kind |
| Achievement series named by Papycha | 115 of 115 | 107 exact, 8 Papycha misspellings |
| Quest-achievement links | 413 agree, 1 disagrees | game objectives `Qf=` / `QF>` |
| Starting characters | 330 of 416 | the rest are objects, typos or descriptions |
| Official steps | 1,090 steps, 6,271 objectives | `QuestSteps`, `QuestObjectives`, `data/text` |

Pairings by hand, checked one by one: "Crocs en jambes" = "Crocs en jambe."
(587), "Faire le tas de pin" = 616, "Fifille à son papa" = 1321, "Gros oeuvre
au château d'Allister" = 132, "La potion Lèche-bottes" = 135, "Laisser les
morts enterrer vivants" = 1614, "Le fabuleux destin d'Amélie Poêlon" = 1266,
"Les cartes spéciales d'Amayiro" = 60, "Les Contretemps du Voyage" = 1520,
"L'essentiel est dans le Lac gelé" = 650, "Métamorphooose !" = 867,
"On recherche Aermyne 'Bracos' Scalptaras" = 119, "On recherche Docteur
Eggob" = 1332, "On recherche YeCh'Ti" = 549, "Parasites de lumière" = 1704,
"Promenades sur les quaies" = 950, "Cogner avant d'entrer" = 868,
"Chaklaplop l'Eau-qui-mord" = 1507. Pages that group several quests take
each part: "Kapla ! | Survivant !" = 934 + 935, "La voie du Wukang / La voie
du Wukin" = 4617 + 4619. Dungeons: Minotoror = 4, Minotot = 53, each Papycha
lair = its "Sombre" and "Céleste" lairs, Moon / Le Chouque / Ul'Khan / Mucane
= their boss (monsters 226, 230, 3647, 3652). "Cavernes Nourricières" was
paired by key to the Tanière Givrefoux; that pairing is wrong and refused.

Not found, they stay French: "Kilucru", "Nourrir la plage", "Les héros
ramassent les orties", "Dévotion aux dieux" (a page over 15 quests), the
grouped "Cueillette…", "Eklate Vulkaine" and "Gare aux Krokilles" pages
(several game quests by tier), the Foulard de Kohrog page, the [Guide] and
[Succès] pages, and the dungeons Cratère Minus, Île de l'Ascension, Antre
de l'infortuné, Troolls de la Chapelle and Troolls de Litneg.

Levels: the game gives a level for 607 quests that have none in the
embedded catalog. Some are wide ranges (15 to 200) rather than an advised
level.

## The rule this must respect

`AGENTS.md` forbids bundling a data file extracted from Ankama's game; its
only carve-out for data is citing quest and achievement titles to order a
guide's pages. `THIRD-PARTY-NOTICES.md` adds that no quest text is
reproduced. The Almanax already shows the way: read at runtime from
Ankama, nothing embedded.

**Decision (recommended by Claude, accepted by the user): nothing from the
game is embedded.** DT Hub reads names, levels and steps from the game's
data server on the player's PC, in the player's language, and keeps them in
its cache. The repository only holds identifiers: which game ids answer
which Papycha page. That table is DT Hub's own work and contains no game
text. Record this as D208.

## Goals

1. In English and Spanish, every name the guides window shows that exists
   in the game is the game's exact name in that language: quest rows,
   prerequisites, previous and next quest, dungeon rows (including the
   four Papycha names after their boss), the dungeon's path label,
   achievement series headers, the header's series and dungeon key, and
   the place names in section titles. French remains only for what the
   game does not name. Nothing the window does not show today gets
   translated: starting characters and bosses are not displayed, so no
   NPC or boss reading is added.
2. Search finds these names in the player's language.
3. Quest rows show a level, in all three languages.
4. Achievement series names come from the game in all three languages,
   which also fixes Papycha's eight misspellings in French.
5. In English and Spanish, a guide can show the quest's official steps
   from the game, in the player's language.

## Non-goals

- Translating Papycha's guide pages. They stay French, with the existing
  "Translate" button.
- Portuguese, German, or any language DT Hub does not speak.
- The Almanax: the app already has it.
- DofusDB, or any second source of names.
- Changing the French list's order or sections.

## Anti-goals

- **An approximate name.** Only exact matches and the hand pairings above
  are used. A new Papycha quest with no pairing stays French until the
  table is regenerated.
- **Hammering Ankama's server.** On 2026-10-21 about 30 MB in a few
  minutes got the IP refused (403 on every path, the game's login included)
  for a while; data center and VPN addresses are refused outright. DT Hub
  asks only for the ids it needs, one class at a time, a handful of
  requests per language, then serves from its cache.
- **Anything tied to the player's account.** Requests carry no token, no
  account, no session: exactly what the game's loading screen sends.
- **Breaking the window when the server is gone.** The server is not a
  public API and may change without notice. Unreachable, refused or
  malformed: the window behaves exactly as today, in French, with no error
  shown; one line in the log.

## The design

### 1. The identifier table (embedded, ids only)

A new embedded file, for example `assets/dofus-touch-ids.json`, maps:

- Papycha quest id → one or more game quest ids (several for grouped
  pages, in display order);
- Papycha dungeon id → game dungeon ids, or a boss monster id;
- Papycha achievement series name → game achievement id (all 115);
- Papycha quest id → game achievement id, for the 50 quests Papycha files
  under no series that the game links to one (see 4).

It is regenerated by a development script under `build/` (Python is
allowed there, never at runtime) that reads Papycha's public API and the
game server and applies a hand-kept file of pairings and refusals (the
lists above). The script prints what it could not pair. It runs before a
release, like the Papycha probe, not in the app.

The script reads whole French classes: Quests (0.7 MB), Dungeons, Items
by key ids, Monsters by the four boss ids (never the whole class: 10 MB),
Achievements (0.4 MB) and AchievementObjectives (0.4 MB). About 2 MB in
six requests, at least a minute apart, stopping at the first refusal,
from a home connection (data center and VPN addresses are refused). The
first table was built on 2026-10-21 from the same reads; the raw dumps
stay out of the repository, they are Ankama's data.

### Server responses

Every `data/map` answer is an object keyed by id (as a string). Fields
used, as read on 2026-10-21:

- `Quests`: `id`, `nameId` (the name), `levelMin`, `levelMax`, `stepIds`.
- `Dungeons`: `id`, `nameId`, `dungeonKey` (an item id).
- `Items`, `Monsters`, `Achievements`, `Areas`, `SuperAreas`: `id`,
  `nameId`.
- `AchievementObjectives`: `achievementId`, `criterion`; a quest link is
  `Qf=<quest id>` (alternatives joined by `|`) or `QF><quest id>,<count>`.
- `QuestSteps`: `id`, `questId`, `nameId`, `descriptionId` (the text
  itself, not an id), `objectiveIds`.
- `QuestObjectives`: `id`, `stepId`, `typeId`, `parameters` (ints),
  `coords` (`{"x": -3, "y": -44}` or empty).
- `QuestObjectiveTypes`: `id`, `nameId`, a sentence with `#1`, `#2`, `#3`
  holes that take `parameters[0..2]`. What each hole names depends on the
  type: NPC for types 1, 2, 3, 9, 12 (`#1`); item for 2, 3 (`#2`) and 20
  (`#1`); monster for 6, 12 (`#2`), 14, 45, 46 (`#1`); area for 5 (`#1`);
  a `data/text` id for 0, 4, 10 (`#1`); the remaining holes are counts.

`data/text` answers `{"<id>": "<text>"}`.

### 2. Reading the game at runtime

A client in Infrastructure, behind an interface in Core, speaks the two
endpoints above. For a language, it asks once for: the quests (names,
levels, step ids), the dungeons, the four boss monsters, the key items
and the achievements of the table, by ids. The result is cached per
language in the cache directory and refreshed with the same 7-day
freshness as the Papycha catalog, in the background, never blocking the
window; names that arrive while the window is open may wait for its next
opening. French is read too, for levels and series names.

Steps are read when a guide asks for them (see 5): the quest's steps,
their objectives, the free texts, and the NPC, item, monster and area
names the objectives cite. They are cached with the rest.

Tests never touch the network: a fake client, as for Papycha, fed with
invented names rather than copies of the game's.

### 3. What the window shows

When a game name exists for the window's language, it replaces the French
one everywhere listed in goal 1. A grouped page joins its parts with the
separator the list already uses. Search indexes the shown names, keeping
the French keys too.

The list has 25 sections, all fixed by the embedded catalog: places
(Astrub, Île de Frigost, Archipel de Vulkania…) and Papycha's own titles
("Quêtes principales", "Quêtes de Sufokia", alignment…). They get English
and Spanish translations in the app's resources, by section id, written
by hand with the game's exact place names (read from `Areas` and
`SuperAreas` on 2026-10-21). A section the API adds later stays French.
Papycha's 22 paths have no game name and stay French.

### 4. Levels and achievement links

A quest row shows a level, in the same column as dungeon rows. The game's
level is used when its minimum and maximum are equal; otherwise Papycha's;
otherwise nothing.

Series headers take the game's achievement name. Papycha keeps its
grouping and order. The 50 quests the game links to an achievement that
Papycha does not file are added to that series, in every language, once
the game's names are in the cache (the series needs its name): each
gets the series name as its `SuccessName`, stays in its section, and
comes after Papycha's ordered quests of that series. Previous/next and
the "n / total" count follow, since they already go by `SuccessName`.
This is the one change the French list sees, besides levels and the
corrected series names. The one disagreement ("Développement durable": Papycha says
"Agriculture ou alchimie", the game says "La maire dénie") keeps
Papycha's choice. The 59 links only Papycha makes stay: those are
achievements that count quests without naming them.

### 5. Official steps

In English and Spanish, a quest guide offers a button next to "Translate"
that shows the quest's official steps in place of the page, and a way back
to Papycha's page. Each step shows its name, its description and its
objectives, written with the game's own sentence for each objective type
(`QuestObjectiveTypes`, e.g. "Talk to #1", "Take #3 #2 to #1") filled with
the cited names, and the objective's map coordinates when the game gives
them. Papycha's page stays the default view.

### 6. Credit

The window credits the game for these names, as the Almanax window credits
krosmoz.com. `THIRD-PARTY-NOTICES.md` gets a section saying what is read
from the game server, that nothing of it is embedded, and that the
identifier table is DT Hub's own.

## Choices Claude made

- Nothing embedded, everything read at runtime (the user asked Claude to
  pick).
- Papycha's page stays the default; official steps behind a button.
- Steps only in English and Spanish.
- Game levels only when precise.
- Game-only achievement links added after Papycha's ordered quests.
- Section titles translated in the app's resources with the game's place
  names; paths stay French.
- No NPC or boss display added: the window shows neither today.
- Game-only links apply in French too, once the cache has the names.
- 7-day refresh, same as the Papycha catalog.

## Left to the builder

Class and file names, the cache format, the exact layout of the steps
view and of the credit line, how the table script is written, and the
order of work.

# Quest rewards in the guides, and a quick level box

Date: 2026-10-23. Status: the user asked Claude to proceed without more
questions ("fais tout, continue"); the choices below marked as Claude's are
open to the user's correction.

## Why

Players want to know what a quest pays before doing it. DOFUS Touch shows a
quest's rewards in its journal, computed for the character's level, but only
for the quest selected in game. The user wants DT Hub's guides window to show
them for any quest, **exactly as the game would** for the chosen character
("que ce soit juste"), without any risk for the player's account.

Later, a second project will rank quests by what they pay against how hard
they really are (a level 200 quest made only of dialogue is easy for
anyone). It needs exact rewards first; it is out of scope here.

The user also wants a quick way to change a character's level on papycha.fr:
the level drives the reward computation, and Papycha's level goes stale (on
2026-10-23 the Cra was 191 in game and 190 on Papycha).

## Where the data comes from

The game's data server, already used for names (D212), answers without any
account:

    POST https://dt-proxy-production-login.ankama-games.com/data/map?lang=fr
    {"class": "QuestSteps", "ids": [2160]}
    {"class": "QuestStepRewards", "ids": [5247]}

Read on 2026-10-23 from a home connection (VPN addresses get 403):

- `QuestSteps`: besides the fields D212 reads, `optimalLevel`, `duration`,
  `xpRatio`, `kamasRatio`, `kamasScaleWithPlayerLevel` and `rewardsIds`.
- `QuestStepRewards`: `id`, `stepId`, `levelMin`, `levelMax` (-1 = no bound),
  `itemsReward` (`[[itemId, quantity], …]`), `emotesReward`, `jobsReward`,
  `spellsReward`, `titlesReward`, `ornamentsReward`, `companionsReward`
  (id lists). A step can have many rewards rows by level bracket ("Distribution
  de Marrons" has 181); a row applies when the character's level is within
  its bounds.

No account, no token: the same requests the game's loading screen sends.
Nothing is embedded (D212 and `AGENTS.md`): everything is read at runtime and
cached on the player's PC.

## The formula, checked against the game

Per step, with `L` the character's level, `opt` the step's `optimalLevel`,
`d` its `duration`, and the ratios read as the server sends them (float32
values such as 0.80000001):

- `fixe(x) = floor(x · (100 + 2x)² / 20 · d · xpRatio)`
- XP when `L ≤ opt`: `fixe(L) · (1 + bonus)`
- XP when `L > opt`: `floor(0.3 · fixe(opt) + 0.7 · fixe(min(L, 1.5 · opt))) · (1 + bonus)`
- kamas: `(K² + 20K − 20) · kamasRatio · d`, with `K = L` when
  `kamasScaleWithPlayerLevel`, else `K = opt`.

`bonus` is the character's XP bonus in percent / 100 (the Bonus Pack gives
+150 %, shown in game under the rewards). A quest's reward is the sum of its
steps.

Checked on 2026-10-23 against the game's journal, to the unit:

| Character | Quest (step) | Game | Formula |
| --- | --- | --- | --- |
| Cra, level 191 | Gare au Kailleu (2160) | 11 512 kamas, 1 976 445 XP | same |
| Eni, level 184 | Distribution de Marrons (1463) | 5 541 305 XP | same |
| Enu, level 190 | Ça rend aimable. (2039) | 4 780 kamas, 1 162 075 XP | kamas same, XP exactly half |

The third case is the only one where the cap `1.5 · opt` applies, and the game
gives exactly twice the formula. Either Touch computes the capped case
differently or that character had a bonus we cannot see. **This must be
settled before release** with one more capped case read in game (a quest whose
level is below two thirds of the character's), and the formula adjusted to
match. Until then the code carries the PC rule above.

The float rounding matters: the 1 162 075 case matches only when 0.7 and
0.3 are float32, as the game computes them. The builder keeps the game's
precision and tests each row of the table above.

## Goals

1. When a quest guide is open and DT Hub has a level for the chosen
   character, a line under the quest title shows its rewards for that level:
   XP, kamas, and the items with their quantities and game names. Emotes,
   spells, titles, ornaments, companions and jobs show as a count of each
   kind ("1 sort, 1 titre"), without names in this version.
2. The line says the level it uses and the XP bonus it applies.
3. A level box in the tracker bar, next to the chosen character, shows that
   character's level; changing it saves it to Papycha at once and the reward
   line follows.
4. Without a Papycha link, the same box keeps a level locally (settings), so
   rewards work for everyone.
5. An XP bonus setting (percent, default 0), remembered, applies to the line.

## Non-goals

- Ranking or recommending quests (the next project).
- Rewards in the quest list rows; only the open guide shows them.
- Names of emotes, spells, titles, ornaments, companions and jobs.
- Reading the character's level or bonus from the game (no OCR, no game
  protocol).
- Editing anything else of the character on Papycha.

## Anti-goals

- **A wrong number.** If a step lacks a field, the server is unreachable, or
  the quest is not paired with a game quest, the line is hidden. Never a guess.
- **Anything tied to the player's account on Ankama's side.** Same rule as D212.
- **Hammering Ankama's server.** Rewards are read for the opened quest only,
  with its steps, then cached; at most two more requests per guide opened
  (`QuestStepRewards`, then `Items` for the reward items).
- **Erasing Papycha data.** Papycha's character save takes every field of the
  character (name, gender, level, class, colour); the level box resends the
  others exactly as read, and a refused save leaves the box on the old value
  with the bar's usual error.

## The design

### 1. Reading the rewards

`TouchNameService` already reads a quest's `QuestSteps` when its official
steps are asked for, and caches them per Papycha quest. It keeps, per step,
the reward fields listed above and reads the step's `QuestStepRewards`, then
the names of the reward items (`Items`). They are cached with the steps and
age with them (7 days).

Today the steps are read only for the official-steps view (English and
Spanish). The rewards need them in every language, French included: opening a
guide asks for them in the background and never blocks the window.

A pure function in Core computes a quest's rewards for a level and a bonus,
from the cached steps. It is where the formula and its tests live.

### 2. The level

The level comes from the chosen Papycha character when the tracker is linked
(`TrackerCharacter.Level`), otherwise from a local setting.

Papycha saves a character with its tracker's AJAX action
`papycha_tracker_character`, `mode=save`, with `character_id`, `name`,
`gender`, `level`, `class_key`, `color` (read from the site's `app.js`
0.38.3). The tracker page's JSON already holds all of them per character;
`TrackerPageParser` reads them too, and `PapychaTracker` gets a save
alongside its status write, with the same nonce handling.

### 3. What the window shows

Under the quest title, when the line has something to say:

    Niv. 191 · bonus +150 % : 1 976 445 XP · 11 512 kamas · 10 × <item>, 1 × <item>

The level box sits in the tracker bar next to the character selector (or, not
linked, at the same place on its own). The bonus setting lives in the same
area. Both are short numeric fields; Enter or leaving the field applies.

### 4. Credit

The game is already credited for names (D212). `THIRD-PARTY-NOTICES.md`
adds that quest rewards are read the same way, nothing embedded.

## Choices Claude made

- Rewards on the open guide only, under the title; quest list untouched.
- Level from Papycha when linked, local otherwise; one box for both.
- XP bonus as a manual setting, default 0: DT Hub cannot see the Bonus Pack.
- Items named, other reward kinds counted.
- The capped-case doubt is settled by one more in-game reading before
  release, not by guessing.

## Left to the builder

Class and file names, the cache format, the exact layout of the line and of
the boxes, the decision number in `docs/DECISIONS.md`, changelog wording in
the three languages, and the order of work.

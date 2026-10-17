# Papycha catalog: switch to the public API by merging

Date: 2026-10-17. Status: approved in conversation, implementation follows.

## Why

On 2026-10-12 papycha.fr closed `wp-json/wp/v2` (401
`papycha_rest_authentication_required`). Everything the quest catalog read
went through it: quests, the hand-kept section pages, the dungeon bodies,
the paths. Since then:

- a player who already had a catalog keeps it, frozen at its last read;
- a new player has an empty Guides tab;
- `build/sonde-papycha` fails, and was skipped for 0.8.4 and later.

Papycha now publishes `wp-json/papycha-public/v1/` (version 0.1.6, no
authentication). Measured on 2026-10-16 and 2026-10-17 against the catalog
DT Hub indexed on 2026-10-11:

| | wp/v2 catalog (11/10) | public API |
| --- | --- | --- |
| Quests | 782 | 762, 757 with the same id, 5 new |
| Dungeons, raids, lairs | 93 | 84, all with the same id |
| Paths | 22 | 22, same ids |
| Hand-kept sections (25, ordered) | yes | no, 41 WordPress categories + 210 zones |
| Achievement, place in it | 505 quests | locked field |
| Prerequisites | 612 | 128 conditions + previous/next quests |
| Level | 118 | 103 |
| Starting character | 693 | locked field |

Missing from the API: the 15 "Dévotion à…" quests, Foulard de Kohrog,
2 [Guide] and 7 [Succès] pages, 2 raids, 6 lairs, Dark Vlad. Sebastopolde
(Papycha) treats these gaps on his side.

The usage policy (`papycha.fr/utilisation-contenus-api/`) requires
attribution and refuses a local copy without authorization. **The user
obtained that authorization from Sebastopolde on 2026-10-17**, for a local
copy used as a fallback, with attribution.

## What the user asked

"Je veux pas casser l'existant, mais que ça se mette à jour." No player
loses a quest, a section, an achievement or an order they have today, and
what Papycha adds or fixes reaches them without a new DT Hub release.

## Design

### Principle: the API refreshes and adds, it never removes

The catalog DT Hub already has is the base. The API is applied on top of
it, matched by id. Nothing absent from the API is removed, and no field the
API leaves empty overwrites a value DT Hub has.

### The base

1. The catalog cache on disk (`cache/papycha-quetes.json`), when it is
   usable (`NeedsRebuild` false).
2. Otherwise an **embedded snapshot**: `assets/papycha-catalog.json`, the
   catalog indexed on 2026-10-11 through wp/v2 (schema 16, 782 quests,
   25 sections, 93 dungeons, 22 paths, section and achievement orders),
   embedded in `DtHub.Infrastructure`. Its site stamp is cleared so that
   the first launch asks the API.

A new player therefore gets the full 11/10 catalog on first open, then the
API's refresh on top. A player whose cache is broken gets the same.

### Merge rules (pure, in Core)

Quests, matched by `Id`:

- **in both**: the API wins on what it fills, the base keeps the rest.
  `Title` (HTML entities decoded), `Url`, `Level` when the API gives one,
  `StartPosition` when the API gives one. `SearchKey` is recomputed from the
  title. `SectionIds`, `SectionId`, `SuccessName`, `ChainStep`, `PlayOrder`,
  `StartPerson`, `Categories`, `Types` stay as they are.
  `Prerequisites`: the base keeps its list when it has one (612 recorded,
  in the form the tracker filter already reads); the API's list is used only
  when the base has none. Merging both would duplicate the same quest under
  two spellings ("Le réveil par la force." and "Le réveil par la force").
- **only in the API**: added, under one section only, as the closed
  indexing did for categories. The API's zone ids are a taxonomy of their
  own (2635 "Cité d'Astrub") and its sections never name the hand-kept
  ones, so the place is learned from the quests both sides know: each of
  their API section and zone ids votes for the sections they sit in, and
  the new quest takes the section its ids gather the most votes for (root
  category 7 not counting, ties by id). With no vote, the base sections it
  names directly, a child before its parent: a quest from Astrub also
  carries "Amakna". Nothing known: the "Autres quêtes" catch-all (`-1`),
  created if the base does not have it. Revised after the branch review:
  matching ids alone would have filed 15 % of the known quests in the
  catch-all, and the least populated section was often the parent.
  Prerequisites: the API's access conditions, then the names of its
  previous quests. No achievement: the field is locked.
- **only in the base**: kept untouched.

Section counts are recomputed after the merge. Section order, achievement
order and every other base field are kept.

Dungeons, matched by `Id`: in both, the API wins on each field it fills
(title without its "[Donjon]" prefix, URL, kind, level, entrance, contact,
key, soul stone size). Only in the API: added. Only in the base: kept.

Paths, matched by `Id`: in both, title and URL refreshed, side kept. Only in
the API: added, side decided by the existing `PathTarget.Of` on the merged
dungeon titles. Only in the base: kept.

### When to ask the API

Same safety rules as today, on the new stamp:

- below `Patience` (15 min) since the last merge: no request;
- otherwise one request to `/status`. Its `last_updated` differs from the
  one kept in the catalog (`SiteModifiedUtc`): merge. Same: nothing. No
  answer: nothing, the catalog stays as it is;
- beyond `Freshness` (7 days): merge, whatever the stamp says.

A failed merge keeps the base, as a failed indexing does today, and is
reported through `LastFailure`.

### Client

`PapychaClient` reads `papycha-public/v1` with `per_page=100`: about eight
requests for the quests, one for the dungeons, one for the paths, one for
the status, versus about fifty today. It decodes HTML entities (the API
still sends `&rsquo;` and `&amp;` in some names). It sends the same
User-Agent as today.

`IPapychaClient` keeps `GetQuestsAsync`, `GetDungeonsAsync`, `GetStampAsync`
and gets `GetPathsAsync` without the dungeon titles (the side is decided at
merge time). `GetSectionsAsync`, `GetPageSectionsAsync` and
`GetCategoryStampsAsync` are removed: the API has no hand-kept section
pages, and the stamp is now global.

### What is removed, because only the dead wp/v2 path used it

- `QuestCatalogService.Arrange` and its helpers (sections from pages,
  achievement order from page groups, widening). The arranged result lives
  on in the snapshot and in every player's cache.
- `IQuestSuccessSeed`, `EmbeddedQuestSuccessSeed`, `QuestSeedEntry`,
  `assets/quest-successes.json`, `build/extract-successes.py`: the snapshot
  already carries every achievement, place and prerequisite they provided.
- The parsers and payloads that only the wp/v2 client or `Arrange` used,
  once the build confirms nothing else references them.
- `QuestIndexingPhase.Sections` and its string, no longer reported.
- `build/sonde-rang`, which measures the achievement ranking `Arrange`
  computed.

### Probe

`build/sonde-papycha` is rewritten against the public API, still through
the shipped client. It checks the assumptions the merge depends on: the API
answers and is enabled; quest, dungeon and path ids still cover the
snapshot's (how many snapshot ids the API still has); counts per family and
per kind; how many quests carry a level and a start position. The
page-shape checks that read article bodies through wp/v2 are dropped: those
bodies are no longer served. `reference.json` is re-blessed.
`build/sonde-voisines` and `build/sonde-papycha/audit-etapes.mjs` read
every article body through wp/v2 to compare with the application: those
bodies are no longer served, so both are removed (they stay in the git
history) and AGENTS.md follows. Rebuilding them on the API's
previous/next quests would be a new tool, not part of this change.

### Attribution

Already in place: the guides window is titled "Guides de papycha.fr" and
every guide opens Papycha's own page. `THIRD-PARTY-NOTICES.md` replaces the
`quest-successes.json` entry with one for `papycha-catalog.json`: source,
date, content, not covered by the MIT licence, embedded with Papycha's
authorization of 2026-10-17.

## Error handling

- API unreachable, 401, malformed JSON: the base stays, `LastFailure` set,
  logged in French as today.
- API reachable but zero quests: the base stays (same guard as today).
- Snapshot unreadable (should never happen, it is embedded): an empty
  catalog, logged, the API merge then fills what it can.

## Testing

Core, with the existing fakes (`FakePapychaClient` reshaped):

- a quest in both keeps its sections, achievement and order, and takes the
  API's title, URL, level and start position;
- an API field left empty does not erase the base's value (level 0, no
  start position);
- the base's prerequisites win; the API's are used when the base has none;
- a quest only in the API is added under its known section, or under
  "Autres quêtes" (created if missing) when none is known;
- a quest only in the base is kept;
- section counts follow the merge;
- dungeons and paths: refreshed, added, never removed; a new path gets its
  side from the dungeon titles;
- no cache: the snapshot is the base and is merged;
- API down: the base is returned untouched and `LastFailure` is set;
- stamp unchanged: no merge; stamp moved: merge; within `Patience`: no
  request.

Infrastructure: the client maps a recorded API page (quests, dungeons,
paths, status) to the core types, entities decoded; the embedded snapshot
loads with 782 quests.

Manual: the application with a fresh `%LOCALAPPDATA%` (Guides tab full),
and with the existing cache (nothing lost, the 5 new quests present).

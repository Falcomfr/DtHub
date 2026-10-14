# Papycha quest tracking in the guides window

**Date:** 2026-10-14
**Status:** design, awaiting the project owner's review

## Goal

Papycha's site has its own quest tracker (`papycha.fr/suivi-quetes/`): an
account per player, unlocked by an access phrase, holding characters and a
status for every quest. DT Hub links to that account once, then lets the
player track quests from the guides window without leaving it: pick the
character at the top, see where each quest stands in the list, have the
quest being read marked "in progress" on its own, and mark it done in one
click. Everything is written to the player's Papycha account, so the site
and DT Hub always show the same progress.

Papycha answered that connecting through the access phrase, or through what
the browser keeps, is fine (relayed by the project owner on 2026-10-14).

## The list stays DT Hub's own

The tracker only reads and writes statuses. The guides list keeps its own
catalogue, its own sections and its own order, which the project owner
finds more coherent than the site's: nothing in this design reorders it.
Marks and filters are laid over it; the "Suivantes" group is added on
top of it and changes nothing below.

This needs nothing more than quest ids, and those are the same everywhere:
the WordPress post id is used by the catalogue, by `papycha-public` and by
the tracker (47974 is "L'île des naufragés" in all three).

Switching the catalogue to `papycha-public/v1` (`wp/v2` answers 401 since
2026-10-12) was considered and set aside on 2026-10-14: the site's usage
policy forbids rebuilding locked fields such as `successes` by extraction,
so the switch would lose the grouping by success and the order DT Hub
builds. The tracker works on the cached catalogue as it is.

## What the site offers (verified 2026-10-14, read only, owner's account)

- **Login.** `GET /suivi-quetes/?pqt_share=<phrase>` answers 302 to
  `?pqt_shared=1` and sets `papycha_tracker_code_v2`: one year, `Secure`,
  `HttpOnly`, `SameSite=Lax`, path `/`. A wrong phrase answers 302 to
  `?pqt_error=1` with no cookie. Creating an account and recovering it by
  e-mail are done on the same page.
- **Reading.** There is no read action. The whole state is embedded in
  `/suivi-quetes/` (about 1.3 MB, `no-store`) as
  `window.PAPYCHA_QUEST_TRACKER = {...};`, about 1.2 MB of JSON:
  - `nonce`, identical for an anonymous and a linked visitor;
  - `space`: `null` when not linked, otherwise `active_character_id` and
    `characters[]` (`id` as a UUID, `name`, `level`, `class_key`,
    `archived`, `quest_statuses` as a map from quest id to status);
  - `views[characterId].progress`: `completed_quests` (explicit and
    inferred), `inferred_quests`, `available_quests` (189 for the owner's
    character with nothing marked), `revision`;
  - `views[characterId].nextQuests`: empty while nothing is marked, shape
    not yet seen.
- **Writing.** `POST /wp-admin/admin-ajax.php`, form encoded:
  `action=papycha_tracker_quest_status`, `nonce`, `character_id`,
  `quest_id`, `status`, with `status` one of `""` (not started),
  `in_progress`, `completed`, `blocked`, `ignored`. The answer is
  `{success, data}`, with `data.message` on failure. Several other actions
  answer with `{character, view}`; whether this one does is unverified.
- **The site infers.** Marking a quest done can mark earlier quests done
  (`inferred_quests`), so the state is read again after every write.
- **The guide pages already carry a status block** when linked:
  `section.pqt-quest-actions` inside `.entry-content`, "Pour quels
  personnages ?" then "Commencer cette quête" and "Terminer". DT Hub keeps
  `.entry-content`, so the block shows today.

## Decisions taken with the project owner

| Question | Answer |
| --- | --- |
| How far to automate | "In progress" as soon as the player moves past the first step; "Done" offered in one click at the last step, never written without a click |
| Which character | A selector at the top, the site's active character by default, then the last one chosen, remembered |
| What the list shows | A mark per quest, a "to do" filter, an "available" filter, the site's suggested next quests |
| Transport | C# with the WebView's cookie (approach A), not JavaScript in the page |

## Design

### 1. Linking the account (approved section)

- **Not linked.** The guides window's bar says what it is for, "Suivez vos
  quêtes en cours et terminées avec votre compte Papycha.", and offers one
  button, "Se connecter au suivi de quêtes Papycha". Nothing else changes.
- **Linking.** The button opens a `WebToolWindow` on
  `https://papycha.fr/suivi-quetes/`, through a new `WebTool.PapychaTracker`
  entry: same shared WebView profile, own pages kept in the window, other
  links sent to the browser like the other tools. The player types their
  phrase or creates the account on Papycha's own page, framed down to its
  content (revised 2026-10-14 after the owner's first try): the site's
  header, menu, sidebars and footer are hidden, the window opens on "J'ai
  déjà une phrase" and "Phrase oubliée", and the creation form waits behind
  a "Créer un compte" button ("J'ai déjà une phrase" brings the sign-in
  back). Under "Ouvrir", a folded "Où trouver ma phrase ?" lists where the
  phrase is shown: the site's tracker page under "Phrase d'accès
  complète", the share link after `pqt_share=`, or the recovery e-mail.
  The site's own design is kept; only the framing is corrected: the window
  takes the colour of the tracker block instead of the site's grey page,
  and the block's bottom margin goes. Window title: "Connexion à
  Papycha", 560 by 820, centred on the guides window, never remembered.
  After each
  navigation the window asks the `CookieManager` for
  `papycha_tracker_code_v2`; as soon as it exists, the window closes itself
  and the guides window fills its bar.
- **What DT Hub keeps.** The site's cookie, in the WebView profile, as any
  browser keeps it. Its value carries the phrase itself (checked on
  2026-10-14: it starts with the phrase's first word), so DT Hub reads it to
  send it back to the site, and never writes it anywhere else: no log, no
  settings. `settings.json` keeps only the id of the last chosen character.
- **Unlinking.** "Délier" in the bar's menu deletes the cookie from DT Hub's
  profile. The Papycha account and its phrase are untouched.
- **Lost link.** A renewed phrase or an expired cookie makes the page come
  back with `space: null`. The bar then shows "Suivi Papycha déconnecté"
  with the same button.

### 2. Tracker client

A new `IPapychaTracker` in `DtHub.Core/Papycha`, implemented in
`DtHub.Infrastructure/Papycha/PapychaTracker.cs` on its own `HttpClient`,
with the same User-Agent as `PapychaClient`. It takes the cookie value as an
argument and knows nothing of WebView2; the App layer reads the cookie from
the guides window's `CoreWebView2.CookieManager` before each call.

- `ReadAsync(cookie)`: `GET /suivi-quetes/` with the cookie, then
  `TrackerPageParser` (pure, in Core) finds `PAPYCHA_QUEST_TRACKER =`, reads
  exactly one JSON value from there with `Utf8JsonReader` and
  `JsonDocument.ParseValue`, and keeps only what this design uses: nonce,
  characters (not archived), active character, and per character the
  statuses, completed, available and next quests. `space: null` gives "not
  linked". A page without the variable gives "unreadable", logged, shown
  as a short message.
- `SetStatusAsync(cookie, nonce, characterId, questId, status)`: the form
  post above. `success: false` returns the site's message. A failure is
  retried once after a fresh read, which renews the nonce.
- When it reads: each time the guides window is shown (at most once a
  minute while linked, every time otherwise, so that "illisible" or
  "déconnecté" recovers on its own), when the linking window closes, in
  the background after every write, failed or not, and just before the
  automatic "in progress", which is the only write the player did not
  click and must never judge from a stale state. Not on every quest
  opened: 1.3 MB per read, compressed on the wire. A character change
  switches between the views the last read already holds.

The cookie is not renewed by DT Hub. If the site ever sends a new one on a
read, it is ignored; the one-year cookie then ends in the "lost link" state
of section 1.

### 3. The bar in the guides window

A new strip under the search bar (`DockPanel.Dock="Top"`), shown in the list
and in a guide alike:

- the character: a drop-down of the non-archived characters (name and
  level), reduced to the name alone when there is only one;
- "Mes quêtes", a visible button: opens the player's tracker in a window
  titled "Mon suivi Papycha". Signed in, the quests live on the site's home
  page (its "dashboard"; `/suivi-quetes/` only manages the account), so the
  window opens there, framed to the tracker alone, on the "Quêtes" tab with
  the "En cours ou bloquées" filter pressed. The bar showing the full
  access phrase and the account tools is hidden, so is the dashboard's
  empty "Accueil" tab. A guide picked there opens in the guides window, not
  in this one. Closing it reads the tracker again. Placed like the other
  tools against the right edge, 600 wide and at most 960 high, remembered
  under `papychaquests`;
- a menu button with "Délier".

In the same bar, before "Mes quêtes", when a quest is shown and the account
is linked (moved there from the quest banner on 2026-10-14, at the owner's
request, so every tracker control sits together):

- the quest's status for the chosen character, as a small label;
- a "Terminée" button, and beside it a small menu for the other statuses
  ("En cours", "Bloquée", "Ignorée", "Non commencée");
- once done, the button turns into a "Terminée ✓" label, still opening the
  menu so that a mistake can be undone.

The site's own block is hidden in DT Hub (`.pqt-quest-actions` added to the
bridge's `HIDDEN` list): it would ask the same thing a second time.

### 4. Automation

- **In progress.** When the player moves, with the step arrows or the step
  list, to an index of 1 or more while a
  quest is shown, and that quest is "not started" for the chosen
  character, DT Hub writes `in_progress`, once per opening. A step restored
  by `ResumeStep` does not count: only a move made in this opening does.
  It never lowers a status: `completed`, `blocked` and `ignored` stay.
- **Done in one click.** On the last step (`StepIndex == StepCount - 1`),
  if the quest is not done, the "Terminée" button takes the accent colour.
  Nothing is written without the click.
- **Guides without steps** (fewer than two) are never set in progress on
  their own; the button is still there.
- After a write the state is read again in the background, so that the
  quests the site inferred get their mark in the list.

Revised 2026-10-14 with the owner: only a move the player asks for starts
the quest, the step arrows or the step list. Scrolling still follows the
step and the "Terminée" highlight, but reading a guide through no longer
leaves it "in progress" on the account. A quest started and then left stays
in progress, as the player did start it.

### 5. The list

Only when linked; otherwise the list is exactly as today.

- **Status per quest**: in words, in a column at the right of the name
  ("En cours", "Terminée", "Bloquée", "Ignorée"), with a check for done
  (explicit or inferred) and a dot for in progress before the name; the
  row dimmed for blocked or ignored.
- **"À faire" filter**: hides the done and ignored quests.
- **"Accessibles" filter**: keeps only the site's `available_quests`.
- The two filters are toggles beside the search box, remembered. A
  category left empty by a filter is hidden while the filter is on.
  They apply to the sections and to "Suivantes", not to a search: a quest
  looked up by name is found even when done, and keeps its mark.
- **"Suivantes"**: the site's `nextQuests` as the first group of the root
  list, absent while the site suggests nothing. Its exact shape is checked
  during implementation, once a quest is marked done on the owner's
  account.

### 6. Errors

The guides never depend on the tracker. Network down, site changed or page
unreadable: the bar shows one short line, the guide and the list behave as
when not linked, and the error is logged. A refused write puts the previous
status back and shows the site's message.

### 7. Tests

In `tests/DtHub.Tests/Papycha`, with inline raw strings cut down from the
captured page, as `QuestPageParserTests` does:

- `TrackerPageParser`: a linked page (two characters, one archived,
  statuses, completed, available), an anonymous page (`space: null`), a
  page without the variable, a variable followed by more script on the
  same line;
- the form body of `SetStatusAsync`;
- the automation rule as a pure function: not started plus a move to step 1
  gives in progress; a restored step gives nothing; done, blocked and
  ignored stay; fewer than two steps gives nothing.

By hand, on the owner's account: link, unlink, move through a guide and see
"En cours" on the site, mark a quest actually done and see it on the site
with its inferred quests in the list, then a renewed phrase giving
"déconnecté".

## Out of scope

- Applying a status to several characters at once.
- Tying a Papycha character to a DT Hub game account.
- The site's objectives, milestones, questionnaire and statistics.
- Creating the account inside DT Hub's own interface: the site's page does
  it.

## Risks

- **`admin-ajax` is not a public API.** The tracker plugin (0.38.0) moves
  fast. Papycha should be told which actions DT Hub uses
  (`papycha_tracker_quest_status`, reading `/suivi-quetes/`), and asked for
  a stable read and write route in `papycha-public` in time.
- **A site change breaks the parser**: the tracker then reads as
  "unreadable", the guides keep working, the log says why.
- **Strings**: every new label goes into `Strings.resx`, `Strings.fr.resx`
  and `Strings.es.resx`; the release goes into the three changelogs.

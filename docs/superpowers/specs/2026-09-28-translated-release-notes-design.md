# Translated release notes

**Date:** 2026-09-28
**Status:** approved design, not implemented

## Goal

The release notes the application shows are in the interface language the
user chose (English, French or Spanish). The notes relayed to the Discord
channel `#changelog` are always in French.

## What exists today

- `CHANGELOG.md` is in English. `livraison.yml` extracts the section of the
  version being shipped into `note.md`, which becomes the body of the GitHub
  release.
- The application reads that body (`ReleaseParser` -> `AppRelease.Notes`) and
  shows it twice: in the "update available" window before installing
  (`App.xaml.cs`, from `UpdateService.Available.Notes`), and in the "just
  installed" window at the next startup, from a file `UpdateService.PrepareAsync`
  writes when the download is verified.
- `discord.yml` sends the release body, as is, when a release is published.
- `docs/DECISIONS.md` (D80) records that release notes exist in one
  version, in English.

## Constraint: installed versions

Every version already installed (0.5.2 and earlier) reads the release body
with its own code, to show the notes of the version it is about to install.
The body must therefore stay plain English markdown: any language markers in
it would reach those users raw, once.

## Design

### Sources

- Two new files at the root, `CHANGELOG.fr.md` and `CHANGELOG.es.md`, same
  structure as `CHANGELOG.md`: a short header, then `## [Unreleased]` and
  `## [x.y.z] - date` sections with the same `### Added / Changed / Fixed`
  headings, translated.
- They start at the next version. Past versions are not translated: only the
  notes of the incoming version are ever shown.
- On `main` today there is no `[Unreleased]` section. The branch
  `feature/ecran-eteint-et-choix-apps` holds one entry (the screen turned off
  while playing): whichever of the two branches lands second adds its French
  and Spanish translation.

### Shipping

- `docs/LIVRAISON.md`, step 2: close `[Unreleased]` in the three files, not
  only the English one.
- `livraison.yml`, step "Release notes": extract the section of the version
  from each of the three files. Write `note.md` (English, the release body, as
  today), `notes.fr.md` and `notes.es.md`. Fail when any of the three files
  does not describe the version, as it already does for English.
- `livraison.yml`, step "Create the release": attach `notes.fr.md` and
  `notes.es.md` beside `DtHub.exe` and `DtHub.exe.sha256`.

### Application

- `AppRelease` gains `NoteUrls`: a dictionary from a language code (`fr`,
  `es`) to the download URL of the asset `notes.<code>.md`. `ReleaseParser`
  fills it from the assets; a release without such assets (every release
  published so far) yields an empty dictionary. Its absence never makes a
  release unusable.
- `Strings` exposes the language it speaks, read only. It already holds it
  privately, and it is the only reliable source: the thread culture is not,
  as `Strings` documents.
- `UpdateService.CheckAsync`: once a newer release is found, if `NoteUrls`
  holds the two letter code of the spoken language, read it with the existing
  `IReleaseSource.ReadAsync` (the one the digest uses). A non-empty result
  replaces `Notes` on the release stored in `Available`. A failure
  (`HttpRequestException`, `TaskCanceledException`) or an empty result keeps
  the English body. Nothing downstream changes: both windows and the notes
  file already read `Available.Notes`.
- English has no asset and reads the body directly.
- Known limit, accepted: if the user changes language between the download
  and the restart, the "just installed" window shows the notes in the former
  language.

### Discord

- `discord.yml` receives the release assets through an environment variable
  (`toJson(github.event.release.assets)`), never through interpolation, like
  the body today.
- It downloads `notes.fr.md` (`browser_download_url`, public once the release
  is published) and sends it. If the asset is missing or the download fails,
  it falls back to the English body, so a release made before this change, or
  a broken asset, still gets announced.
- The cut footer is written in French. The 3800 character cut logic is
  unchanged.

### Written rules

- The header paragraph of `CHANGELOG.md` ("exist in a single version").
- `AGENTS.md` and `CONTRIBUTING.md`, language section: the two translated
  changelogs join the exceptions to "the repository is written in English".
- `docs/DECISIONS.md`: a new entry, D176, in French like the rest of the file,
  which reverses the line of D80 on release notes and gives the reason.

## Testing

TDD, full suite green before any commit.

- `ReleaseParserTests`: a release with `notes.fr.md` and `notes.es.md` assets
  fills `NoteUrls` with both; a release without them yields an empty
  dictionary and is still usable.
- `UpdateServiceTests`, with the existing fake source:
  - spoken language `fr`, asset present: `Available.Notes` is the French text;
  - read fails: `Available.Notes` stays the English body;
  - spoken language `en`: no extra read.
- `livraison.yml` and `discord.yml` have no automated test. Their scripts are
  run locally: the extraction against the real changelog files, the Discord
  script against a fake payload with and without the French asset, printing
  the embed instead of posting it.

## Out of scope

- Translating past versions.
- Showing the French or Spanish notes on the GitHub release page.
- Machine translation: translations are written in the repository when a
  version is shipped.

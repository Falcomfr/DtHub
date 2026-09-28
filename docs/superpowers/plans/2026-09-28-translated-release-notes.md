# Translated Release Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The application shows release notes in its interface language, and Discord receives them in French.

**Architecture:** French and Spanish changelogs live beside `CHANGELOG.md`. The release pipeline attaches `notes.fr.md` and `notes.es.md` to each release while the body stays English. The application reads the asset of its language when it finds a newer release, and the Discord relay sends the French one, both falling back to the English body.

**Tech Stack:** .NET 10, xUnit, GitHub Actions (pwsh on the release job, Python 3 on the Discord job).

**Spec:** `docs/superpowers/specs/2026-09-28-translated-release-notes-design.md`

## Global Constraints

- The release body stays plain English markdown: installed versions (0.5.2 and earlier) read it.
- Asset names are exactly `notes.fr.md` and `notes.es.md`. English has no asset.
- Past versions are not translated. The new changelogs start empty of versions.
- Every file created follows `.editorconfig`: UTF-8 with BOM, LF, final newline.
- Repository text in English, except `docs/DECISIONS.md` entries (French) and the two translated changelogs. Test method names in French, like the rest of the suite. No em dash anywhere.
- .NET commands go through a log, never `rtk dotnet` (see `CLAUDE.md`):
  `rtk proxy dotnet test > /tmp/test.log 2>&1; grep -iE "erreur|échec|réussi|failed|passed|total" /tmp/test.log | tail -8`
- Before each commit touching C#: `rtk proxy dotnet format DtHub.slnx --verify-no-changes > /tmp/format.log 2>&1` must report nothing.
- Git through `/usr/bin/git`. Commit messages in the repository's style (an English sentence starting with a verb), ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- A culture with a region (`fr-FR`, `es-ES`) must find `notes.fr.md` / `notes.es.md`: the lookup uses `TwoLetterISOLanguageName`. Pinned in Task 2.
- A manual check (`automatic: false`) feeds the "update available" window: the translated note must be in `Available` then too, not only after a download. Pinned in Task 2.
- An asset named with other casing (`Notes.FR.md`) is still found, keyed `fr`. Pinned in Task 1.
- A section present but empty in one language means a forgotten translation: the pipeline refuses it, in all three files. Pinned in Task 3's verification.
- A release published before this change has no French asset: Discord still announces it, with the English body. Pinned in Task 4's verification.

---

### Task 1: The parser finds the translated notes

**Files:**
- Modify: `src/DtHub.Core/Updates/AppRelease.cs`
- Modify: `src/DtHub.Core/Updates/ReleaseParser.cs`
- Test: `tests/DtHub.Tests/Updates/ReleaseParserTests.cs`

**Interfaces:**
- Produces: `AppRelease.NoteUrls`, an init-only property `IReadOnlyDictionary<string, string>` (language code in lower case -> `browser_download_url`), empty by default. A property and not a positional parameter, so every existing `new AppRelease(...)` keeps compiling.

- [ ] **Step 1: Write the failing tests** in `ReleaseParserTests`

```csharp
[Fact]
public void Lit_les_notes_traduites_jointes_a_la_livraison()
{
    var json = Json.Replace(
        "\"assets\": [",
        """
        "assets": [
            { "name": "notes.fr.md", "size": 120,
              "browser_download_url": "https://exemple/notes.fr.md" },
            { "name": "Notes.ES.md", "size": 118,
              "browser_download_url": "https://exemple/notes.es.md" },
        """,
        StringComparison.Ordinal);

    var release = ReleaseParser.Parse(json, "DtHub.exe");

    Assert.NotNull(release);
    Assert.Equal("https://exemple/notes.fr.md", release.NoteUrls["fr"]);
    Assert.Equal("https://exemple/notes.es.md", release.NoteUrls["es"]);
    Assert.Equal(2, release.NoteUrls.Count);
}

[Fact]
public void Une_livraison_sans_notes_traduites_reste_utilisable()
{
    var release = ReleaseParser.Parse(Json, "DtHub.exe");

    Assert.NotNull(release);
    Assert.Empty(release.NoteUrls);
}
```

- [ ] **Step 2: Run the tests, expect a build failure** (`NoteUrls` does not exist).

- [ ] **Step 3: Implement.** Add the property to `AppRelease` (document it in the record's summary like its other members). In `ReleaseParser.Parse`, set it with an object initialiser on the `new AppRelease(...)`, from a private `NoteUrls(JsonElement root)` that walks `assets` once and keeps names matching `^notes\.([a-z]{2})\.md$` (`RegexOptions.IgnoreCase`, `[GeneratedRegex]` with a 2000 ms timeout like `ReleaseNotes`), key `ToLowerInvariant()`, skipping an empty `browser_download_url`. The class becomes `static partial`.

- [ ] **Step 4: Run the full suite**, expect all green.

- [ ] **Step 5: Commit** `Read the translated notes a release carries`.

---

### Task 2: The update service fetches the note in the spoken language

**Files:**
- Modify: `src/DtHub.Core/Localization/Strings.cs` (make `Spoken` public)
- Modify: `src/DtHub.Infrastructure/Updates/UpdateService.cs` (`CheckAsync`)
- Modify: `tests/DtHub.Tests/Fakes/FakeReleaseSource.cs`
- Test: `tests/DtHub.Tests/Updates/UpdateServiceTests.cs`

**Interfaces:**
- Consumes: `AppRelease.NoteUrls` (Task 1).
- Produces: `public static CultureInfo Spoken` on `Strings`, same semantics as today (the chosen culture, else `CultureInfo.CurrentUICulture`). `FakeReleaseSource.WithFailure(string url)`: `ReadAsync` of that URL throws `HttpRequestException`.

- [ ] **Step 1: Write the failing tests.** `Monter` gains an optional `IReadOnlyDictionary<string, string>? noteUrls = null` parameter set on the release (`NoteUrls = noteUrls ?? new Dictionary<string, string>()`). The class implements `Dispose` already: add `Strings.Speak(null);` to it. Tests:

```csharp
private static readonly Dictionary<string, string> Traduites = new()
{
    ["fr"] = "https://exemple/notes.fr.md",
    ["es"] = "https://exemple/notes.es.md",
};

[Theory]
[InlineData("fr", "Une chose, en français.")]
[InlineData("fr-FR", "Une chose, en français.")]
[InlineData("es-ES", "Una cosa, en español.")]
public async Task Annonce_la_note_dans_la_langue_parlee(string langue, string attendu)
{
    Strings.Speak(CultureInfo.GetCultureInfo(langue));
    var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
    _ = source.WithText("https://exemple/notes.fr.md", "### Ajouté\n- Une chose, en français.");
    _ = source.WithText("https://exemple/notes.es.md", "### Añadido\n- Una cosa, en español.");

    // Manual check: the "update available" window reads Available too.
    await service.CheckAsync(automatic: false);

    Assert.Contains(attendu, service.Available!.Notes, StringComparison.Ordinal);
}

[Fact]
public async Task Garde_l_anglais_quand_la_note_traduite_ne_repond_pas()
{
    Strings.Speak(CultureInfo.GetCultureInfo("fr"));
    var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
    _ = source.WithFailure("https://exemple/notes.fr.md");

    await service.CheckAsync(automatic: false);

    Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
}

[Fact]
public async Task Garde_l_anglais_quand_la_note_traduite_est_vide()
{
    // FakeReleaseSource answers an empty string for an unknown URL.
    Strings.Speak(CultureInfo.GetCultureInfo("fr"));
    var (service, _) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);

    await service.CheckAsync(automatic: false);

    Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
}

[Fact]
public async Task En_anglais_garde_le_corps_de_la_livraison()
{
    // Both translations are served: English must still not pick one.
    Strings.Speak(CultureInfo.GetCultureInfo("en"));
    var (service, source) = Monter(Empreinte(Neuf), new Version(0, 2, 0), Traduites);
    _ = source.WithText("https://exemple/notes.fr.md", "- Une chose, en français.");
    _ = source.WithText("https://exemple/notes.es.md", "- Una cosa, en español.");

    await service.CheckAsync(automatic: false);

    Assert.Equal("### Ajouté\n- Une chose.", service.Available!.Notes);
}
```

Plus, beside `Ecrit_la_note_de_version_et_ne_la_rend_qu_une_fois`, a `[Fact] Ecrit_la_note_traduite_pour_le_demarrage_suivant` that speaks `fr`, serves the French note, runs `CheckAsync(automatic: true)`, then asserts the next version's `TakeNotes()` contains `"Une chose, en français."`.

- [ ] **Step 2: Run the tests, expect failures** (`WithFailure` missing, then notes still English).

- [ ] **Step 3: Implement.**
  - `Strings.Spoken`: `private` -> `public`, keep its doc comment, adjust it to say `UpdateService` reads it too.
  - `FakeReleaseSource`: a `HashSet<string>` of failing URLs; `ReadAsync` throws `new HttpRequestException("...")` for them.
  - `UpdateService.CheckAsync`: after the source tree guard and before `Available = latest`, `latest = await TranslatedAsync(latest, cancellationToken)`. The private `TranslatedAsync(AppRelease release, CancellationToken)` looks up `Strings.Spoken.TwoLetterISOLanguageName` in `release.NoteUrls`; on a hit, `ReadAsync`, and returns `release with { Notes = text.Replace("\r\n", "\n").Trim() }` when non-empty; otherwise the release unchanged. It catches `HttpRequestException` and `TaskCanceledException` only, logs through a new `[LoggerMessage]` in French like its neighbours (`"Note de version en {Langue} illisible, l'anglais reste."`, level Information), and returns the release unchanged.
  - Update the class summary's thread paragraph with one sentence: the note comes in the spoken language when the release carries it.

- [ ] **Step 4: Run the full suite**, expect all green, then the format check.

- [ ] **Step 5: Commit** `Show the release notes in the interface language`.

---

### Task 3: Changelogs, release pipeline and written rules

**Files:**
- Create: `CHANGELOG.fr.md`, `CHANGELOG.es.md`
- Modify: `.github/workflows/livraison.yml` (steps "Release notes" and "Create the release")
- Modify: `CHANGELOG.md` (header paragraph), `docs/LIVRAISON.md` (step 2 and the "two files" paragraph), `AGENTS.md` and `CONTRIBUTING.md` (language section), `docs/DECISIONS.md` (new D174 at the end)

**Interfaces:**
- Produces: assets `notes.fr.md` and `notes.es.md` on every release from now on (consumed by Tasks 1, 2 and 4).

- [ ] **Step 1: Create the two changelogs.** Header only, no version section (on `main` there is no `[Unreleased]` yet). Header of `CHANGELOG.fr.md`, in French: title `# Journal des modifications`, one sentence saying it is the French translation of `CHANGELOG.md`, published with each release and shown in the application in French, starting at the version after 0.5.2, headings `### Ajouté`, `### Modifié`, `### Corrigé`. Same for `CHANGELOG.es.md` in Spanish: `# Registro de cambios`, headings `### Añadido`, `### Cambiado`, `### Corregido`.

- [ ] **Step 2: Rewrite the "Release notes" step** of `livraison.yml`: the same extraction as today, run for `CHANGELOG.md -> note.md`, `CHANGELOG.fr.md -> notes.fr.md`, `CHANGELOG.es.md -> notes.es.md` (three plain calls to one local function, no loop over a hash that hides which file failed). It throws `"<file> does not describe version <v>."` when the heading is missing, and `"<file> has an empty section for version <v>."` when the trimmed section is empty. Update the step's comment. In "Create the release", add `notes.fr.md` and `notes.es.md` after `publication/DtHub.exe.sha256`.

- [ ] **Step 3: Verify the extraction locally.** Copy the step's script to the scratchpad with `$env:VERSION` set, run it with `powershell.exe -NoProfile -File` from a scratch folder holding three copies of the changelogs where a `## [9.9.9] - 2026-09-28` section exists (filled in all three, then empty in the French one, then missing from the Spanish one). Expected: three files written; then the "empty section" error naming `CHANGELOG.fr.md`; then the "does not describe" error naming `CHANGELOG.es.md`. Redirect output to a log and grep it, per `CLAUDE.md`.

- [ ] **Step 4: Update the written rules.**
  - `CHANGELOG.md` header: entries are written in English and translated in `CHANGELOG.fr.md` and `CHANGELOG.es.md` from the version after 0.5.2; the English section is the release body, the translations travel as assets and are what the application shows in French or Spanish, and what Discord receives.
  - `docs/LIVRAISON.md` step 2: close `[Unreleased]` in the three changelogs; the pipeline refuses to ship when one of them is missing the version or has it empty. The paragraph listing the release files: four files, and only the first two are required by the application.
  - `AGENTS.md` and `CONTRIBUTING.md`: the translated changelogs join the exceptions to "the repository is written in English", with the reason in one line (they are shown to users in their language). `AGENTS.md` says "Five things stay French": make the count and the list true.
  - `docs/DECISIONS.md`, `## D174 - Les notes de version parlent la langue de l'application`, dated 2026-09-28, "Acceptée", in French: the need (notes shown in the chosen language, Discord in French), what D80 said, why the body stays English (installed versions), why assets rather than markers in the body or text built into the executable, the known limit (language changed between download and restart).

- [ ] **Step 5: Commit** `Ship the release notes in three languages`.

---

### Task 4: Discord announces in French

**Files:**
- Modify: `.github/workflows/discord.yml`

**Interfaces:**
- Consumes: the `notes.fr.md` asset (Task 3), via `github.event.release.assets`.

- [ ] **Step 1: Change the step.** Add `ASSETS: ${{ toJson(github.event.release.assets) }}` to `env`. In the Python script, before the cut: find the asset named `notes.fr.md` in `json.loads(os.environ.get("ASSETS") or "[]")`, fetch its `browser_download_url` with `urllib.request` (same `User-Agent` as the webhook call, `timeout=30`), decode `utf-8-sig`, strip; use it as `notes` when non-empty. On `urllib.error.URLError`, `TimeoutError`, or a missing asset, print one line saying so and keep the English body. French texts: default title `"Nouvelle version"`, footer `f"Coupé à {len(corps)} caractères sur {entier}. La suite est sur GitHub."`. Update the header comment: the notes sent are the French ones, and why the English body is the fallback.

- [ ] **Step 2: Verify locally.** Copy the script to the scratchpad, run it with `python3` against a scratch HTTP server (a few lines of `http.server` that serves a `notes.fr.md` on GET and prints the POSTed JSON), in three runs: French asset served (embed carries the French text); `ASSETS` empty `[]` (embed carries `NOTES`); asset URL answering 404 (embed carries `NOTES`, one line printed). A fourth run with a 5000 character French note shows the French footer.

- [ ] **Step 3: Commit** `Announce releases on Discord in French`.

---

## After the tasks

Full suite green, format check clean, then the branch is ready for `superpowers:finishing-a-development-branch`. The screen-off entry from `feature/ecran-eteint-et-choix-apps` is translated into the two new changelogs by whichever branch reaches `main` second.

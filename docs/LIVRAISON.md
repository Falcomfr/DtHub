# Ship a release

DT Hub updates itself from the repository's releases. The application asks
for the latest one at startup, downloads it in the background if the
"Update itself automatically" box is checked, verifies its checksum, and
installs the new executable on exit. The release note appears at the next
startup, the one that finally runs the new version.

## What only needs doing once

The repository must be **public**: the application queries the API without
a token, and a token embedded in the executable would be readable by anyone
who opens it. Its account and its name are written in
`src/DtHub.Core/Updates/ReleaseChannel.cs`.

As long as the repository does not exist, the request returns "nothing to
report" and the application knows no more than that: nothing breaks,
nothing is shown.

## Ship it

0. Run the probe: `dotnet run --project build/sonde-papycha`. It queries
   the real site and returns 1 if the application no longer finds what it
   assumes to be there. A legitimate discrepancy is reblessed with
   `-- --benir`.
1. Bump the version in `Directory.Build.props`, field `VersionPrefix`.
2. Close the `## [Unreleased]` section of the changelog: rename it to
   `## [0.2.0] - 2026-09-02`. The release pipeline reads the release note
   there, and refuses to ship if it cannot find it.
3. Commit both, then tag with the same number:

   ```
   git tag v0.2.0
   git push origin main --tags
   ```

The pipeline builds, publishes, computes the checksum and creates the
release with two files: `DtHub.exe` and `DtHub.exe.sha256`. These are the
two names the application expects; a release missing either one is
ignored.

## What the file is published with

The options live in a single file,
`src/DtHub.App/Properties/PublishProfiles/win-x64.pubxml`, which the
pipeline, the development launcher and `AGENTS.md` all three point to:

```
dotnet publish src/DtHub.App -p:PublishProfile=win-x64 -o publication
```

They used to live copied across these three places, with nothing checking
that they agreed. A publish without `SelfContained` produces an executable
of one hundred fifty kilobytes that requires .NET to be installed on the
machine, while carrying exactly the same Windows metadata as the real one:
the identity check, which only reads that metadata, let it through.

So the pipeline also measures what the file is: a single file in the
publish folder, and at least forty megabytes. The two together are enough,
since a framework-dependent binary weighing one hundred fifty kilobytes
drags along some forty companion files.

Translations, meanwhile, are checked at build time and not on the binary:
searching for `fr/DtHub.Core.resources.dll` in the bytes falls back on the
embedded `deps.json`, which lists the satellites even when
`SatelliteResourceLanguages` excluded them. Verified: a file published
without them weighs half a megabyte less and still passes the search. So
the check is on the property itself, in `src/DtHub.App/DtHub.App.csproj`,
alongside the ones for embedded resources.

## What the application does with all this

**It does not update itself from a source tree.** If the solution file
sits above the executable, the update is refused: the development launcher
republishes on every startup and would overwrite it within seconds, giving
the impression of a regression.

**It verifies before installing.** The checksum of the downloaded file is
compared against the one the release announces. On a mismatch, the file is
deleted without having been used.

**It never replaces itself mid session.** A running executable cannot be
overwritten, but it can be renamed: on exit, the old one moves aside as
`DtHub.exe.ancien`, the new one takes its place, and the next startup
sweeps away what remains. If the second half fails, the first is undone.

**None of this counts as a failure.** No network, missing repository,
quota reached, wrong checksum, locked file: the application carries on
with the version it has.

## Getting the binary recognized

What Windows and antivirus software make of it, and how to sign it, are in
[CONFIANCE.md](CONFIANCE.md). The pipeline already refuses to ship a
binary missing its name, description or publisher, and signs it if the
`SIGNING_COMMAND` secret is set on the repository.

## The release comes out as a draft, and it must be published

**The pipeline does not distribute anything by itself.**
`gh release create` passes `--draft`, and the application queries
`/releases/latest`, which ignores drafts: nothing reaches anyone until
the next command has been typed.

This is the only window in which one can examine **the exact artifact**
that users will receive, rather than a local rebuild that would not carry
the same checksum. Three steps, in this order:

1. download the binary from the draft;
2. send it to VirusTotal. **Warning, this upload publishes it** to the
   service's subscribers: no consequence for code that is already public,
   but not to be done on a private binary. One or two marginal engines
   flagging an unsigned self-extracting .NET executable is unremarkable;
   a major engine, or a dozen, needs to be understood before publishing;
3. submit it to Microsoft's "Submit a file for analysis" portal, without
   waiting for an alert. It is free, it takes a few days, and Defender
   will decide the fate of the large majority of downloads.

Then, only then:

```
gh release edit v0.3.0 --draft=false
```

At that moment the version becomes visible to automatic updates.

## The tag must say what the repository declares

The pipeline compares the tag's number against the `VersionPrefix` in
`Directory.Build.props` and refuses to ship if they differ.

This is not a theoretical precaution: the repository once lived with a
`VersionPrefix` of 0.2.0 and thirty seven feature commits on top of it,
all described under "Unreleased". Tagging `v0.2.0` would have shipped a
binary whose release note lied.

## Checking a release by hand

```
gh release view v0.2.0
sha256sum DtHub.exe
```

The checksum shown must match the one in the release's `.sha256` file.

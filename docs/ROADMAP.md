# Roadmap

Versioning follows [SemVer](https://semver.org/). As long as the
major version is `0`, the interface and the configuration formats can
change.

## v0.3 - What works today

- Detection of phones over USB and Wi-Fi, assisted pairing, automatic
  reconnection even when the port changes after a restart.
- Detection of game instances, one per Android profile, whatever the
  profile number.
- Opening each instance in its own window, on its own virtual display.
- Overlaid windows, anchored to a nine-position grid, or gathered into
  a tabbed frame.
- Floating configurator with three tabs, recalled by shortcut.
- Adjustable quality tier, global or per account, including resolution
  and bitrate.
- Recovery of a window that the connection has dropped.
- Device check before launch: battery, heat, free space, Wi-Fi band,
  power-saving setup.
- Guides window reading papycha.fr, with where you left off and what
  each quest unlocks.
- Today's Almanax, read from the official portal and filtered for
  DOFUS Touch.
- Play time per account.
- Saving and restoring settings.
- Incident report, redacted of anything identifying, to copy and send.
- Single-file publishing, no installer, updates from the repository,
  and continuous integration that tests and ships.
- Tested on two real phones, Android 11 and Android 16.

## v0.4 - What is still missing

- On-screen indication of the active instance.
- Faster guide indexing: the API's `modified_after` filter only hits
  the pages that changed, and dungeons account for four fifths of the
  time. See D141.

## v1.0 - Stable

- Configuration formats frozen and migrations handled.
- User documentation finalized.
- Full diagnostics.

## In progress, outside the code

- Publishing the repository, a presentation page, and a first
  downloadable release.
- Authenticode signing through SignPath Foundation, free for open
  source projects, which requires a public repository and an already
  shipped release. See `docs/CONFIANCE.md`.

## Out of scope, permanently

Any game automation: bots, macros, repeating actions, screen
recognition to play, input synchronization between accounts. This is
not a matter of priority, it is a limit of the project.

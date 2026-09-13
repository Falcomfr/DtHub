# Modifications made to scrcpy

**Status as of 2026-08-29, scrcpy v4.1: no modification.**

DT Hub uses scrcpy as published by the upstream project. The official
archive is downloaded, its checksum verified, and extracted without altering
a single file. The `LICENSE.txt` provided upstream is kept alongside the
executable.

This file exists for two reasons: to document this absence of modification,
and to set out the procedure to follow should one become necessary, since
the Apache 2.0 licence requires that any modified file be flagged.

## Why no modification is necessary

The need that could have required one is launching an application on an
Android user other than the main one: a clone, a managed profile, a second
space. scrcpy's `--start-app` option does not take a user id, and evolving
it would require forking the Java server, hence an Android build toolchain
and permanent tracking of upstream.

This is not necessary, because scrcpy can create a virtual display and
publishes its id. In `NewDisplayCapture.java`, when the display is created:

```java
Ln.i("New display: " + width + "x" + height + "/" + dpi + " (id=" + virtualDisplayId + ")");
```

This line is relayed to the client, so it is readable in the process's
output.

DT Hub therefore proceeds as follows:

1. `scrcpy --new-display=<size>/<density> --no-vd-system-decorations`;
2. read the display id from scrcpy's output;
3. `adb shell am start --user <id> --display <display> -n <component>`.

The application is launched by ADB, which has always accepted `--user`.
Any integer user id works, with no particular value assumed.

This path touches neither scrcpy nor the targeted Android application.

## If a modification became necessary

To do, in this order:

1. describe here the need and why no other path is suitable;
2. produce a reproducible patch and place it in this folder, in the form
   `NNNN-description.patch`, applicable to a specific upstream tag;
3. state here the upstream tag concerned, the command to apply it, and the
   rebuild procedure;
4. flag the modified files at the top of each one, as required by section
   4b of the Apache 2.0 licence;
5. update `THIRD-PARTY-NOTICES.md` and `docs/DECISIONS.md`.

Without these five points, the modification must not be merged.

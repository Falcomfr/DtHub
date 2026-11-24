# Let Samsung players turn off the game optimizing service

On 2026-11-23 and 24, a player on a Galaxy S21 FE (SM-G990B2, Android 16) found
the main-profile copy of the game blurry on the PC, and his work-profile copy
sharp. DT Hub sent both the same picture (1912x976, 12316 kb/s), and the
Android game mode was `standard` on both profiles. A renamed copy
(`com.ankama.dofustouch.clone1`) on the same main profile was sharp. Disabling
Samsung's game optimizing service (GOS, `com.samsung.android.game.gos`) with
`pm disable-user --user 0` and restarting the phone made the original sharp
too. GOS lowers the game's render resolution, found by package name, on the
main profile only, outside the Android game modes that D203 already handles.
DT Hub cannot see it: the layer geometry reads `couche ?` on these Samsungs, so
the 75 % warning never shows.

## Goals

- A player whose original game is blurry on a Samsung can turn GOS off from
  DT Hub, and turn it back on.
- The next log a Samsung player sends says whether GOS is on, off or absent.

## Non-goals

- No automatic disabling, and no re-enabling after a Samsung update.
- No detection of the actual render resolution.
- No other brand.

## Anti-goals

- Offering to change the phone's system to players who do not have the
  problem.
- A button the player cannot find, like the game mode section that only shows
  once the account has been opened.

## Approaches

- **B. Read the GOS state when the phone connects (picked).** One shell
  command per Samsung when DT Hub first sees it, kept per phone and read again
  after the button acts. The log line comes from that same read, and the
  button is there before any account is opened.
- A. Read it with the game modes, after a window opens. The least code, but
  the button stays hidden until the account is open, and the log line only
  comes at launch.
- C. Read it when the account's settings popup opens. A popup that fills in
  late, more interface code, and still no log line at connection.

## Design

- **State.** Per phone: on, off, or absent. Read with
  `pm list packages -e --user 0 com.samsung.android.game.gos` (on) and
  `-d` (off); neither listing it means absent. Only on phones whose
  manufacturer is Samsung.
- **When.** When DT Hub first sees a Samsung, and again right after the button
  acts. Not polled.
- **Log.** One line per Samsung connection, with the state.
- **Where.** In the account's settings popup (the two-sliders chip), right
  under "Mode jeu du téléphone", with a separator and its own heading.
- **Who sees it.** Only an account that launches the original
  `com.ankama.dofustouch` in profile 0 of a Samsung where GOS is on.
  Renamed copies and other profiles are not affected, so they do not see it.
- **Turn off.** A confirmation first: the picture should be sharp, Game Booster
  may work less well, a Samsung update may turn it back on, and the phone must
  be restarted. Then `pm disable-user --user 0 com.samsung.android.game.gos`.
  DT Hub reads the state again rather than trusting the command's output
  (its exact text on Samsung is unknown). If it is still on, say so with the
  manual way.
- **Turn back on.** While GOS is off, the same accounts see "Réactiver"
  instead, which runs `pm enable com.samsung.android.game.gos` and reads the
  state again. No confirmation needed: it restores the phone's default.
- **After either.** A notice: restart the phone for it to take effect.
- Windows and Mac/Linux both; strings in en, fr, es; log message in the three
  log languages.

## Left to the builder

- The exact place in the code where a Samsung is "first seen", and how the
  per-phone state reaches the rows.
- Wording of the heading, button, confirmation and notices, kept in the tone
  of the game mode section.
- The next decision number (D237) and the CHANGELOG entry.

Not verified on a real phone: that `pm disable-user` is accepted on every
One UI version, and that a restart is really needed (it was what worked).

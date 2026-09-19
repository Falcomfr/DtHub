# Changelog

All notable changes to this project are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [semantic versioning](https://semver.org/).

Entries are written in English from 0.3.0 onward: they become the release notes
published on GitHub and shown inside the application, and those exist in a
single version. The 0.2.0 and 0.1.0 sections predate that rule and stay in
French.

## [0.5.0] - 2026-09-19

### Added

- **The panel now says what each account is doing, and for how long.** A dot on
  the row for the state, and a second line that names a reconnection, an opening,
  or how long the window has been up. The line was already there and stood empty
  unless the account had been renamed, so it costs no height: the row is in fact
  a pixel shorter than before, because the two drag hints stopped reserving
  fourteen pixels per row for a mark that only shows during a drag.

- **Each phone's line says how it is doing.** Beside the charge it already
  carried: the Wi-Fi link at all times, a thermometer once Android says it is
  throttling, and the free space once there is little of it left. All three were
  already being read every minute and thrown away once they had been turned into
  warnings, so a phone with nothing wrong had nothing to show at all.

  Heat is never given as a temperature: the reference phone reads 84 °C while
  nothing is being throttled. And the link names what is wrong with it, a crowded
  channel or the shared band, rather than the band it happens to be on.

- **Every account can carry a colour**, on its row and on its tab in the frame.
  Six tints, chosen so that none of them can be mistaken for the blue of a
  selection or for the three status colours. New accounts are given one, and so
  are the accounts you already have. A seventh account is left unmarked rather
  than handed a duplicate, and an account you strip of its colour stays stripped:
  the application only decides for accounts nobody has decided for.

### Changed

- **The launch and stop buttons now stand out from the settings beside them.**
  They are raised, where the rest of the row is flat, and they share one place
  instead of moving as windows open and close. The per-account quality and
  distance moved into a single chip, with the colour, so a row carries three
  controls and not five.

- **The dungeon list reads in columns.** Level, size and coordinates each have
  their own, right-aligned, with the coordinates in a fixed-pitch face so the
  brackets line up. The band headers are no longer smaller than the rows they
  head, and the list finally answers to the pointer and to the keyboard with the
  application's own colours rather than Windows' pale blue.

- **The panel fades in and out on Ctrl+P**, tabs cross over instead of blinking,
  and a row fades in when a phone is found. Nothing else moves.

### Fixed

- **A phone could be told its Wi-Fi channel was crowded when it was not.** The
  share of retransmitted frames is counted from the moment the phone associates,
  so the first seconds of a video stream are a burst against an almost empty
  sample: a panel showed 41 % on a link that read 15 % once it had run for a
  minute. The figure is now believed only past ten thousand frames, about a
  minute of streaming.

- **The chosen language only reached what was built at startup.** A panel set to
  English showed "Devices" and "Shortcuts" beside "Connecté en Wi-Fi" and
  "ouvert à l'instant": the labels written into the windows were built while the
  language was still set, everything the device sweep produced afterwards ran
  from a timer callback and came back in Windows' language. What the application
  decided to speak no longer depends on which thread asks.

## [0.4.0] - 2026-09-14

### Added

- **The distance in the game is now set per account, like the quality tier.**
  You want ground on the account you play, and the mules whose health bar is
  all you watch do not need it. An account follows the shared distance until it
  is given one of its own.

  Changing it does not reopen the window. The shared setting does, because a
  setting that shows nothing looks dead, but reopening force-stops the game and
  disconnects the character, which is not a price to pay on the account you are
  playing. A dot on the account instead says the open window is still running
  with the previous distance.

### Removed

- **The "Log the frame rate" setting is gone.** Half of what it promised was
  untrue: its help said the log would also note the encoders the phone offers,
  and that happens on every launch whether the box is ticked or not. What was
  left was one log line per second and per window, for a number whose zero reads
  as a fault when it is not one. Settings files that still carry the old value
  are read as before; the value is ignored and disappears on the next save.

### Fixed

- **A game window stayed black for a long time and nothing said why.** The log
  measured the display being ready and the game's start command returning, both
  of which happen seconds before anything is drawn, then declared the launch a
  success. scrcpy announces its first decoded frame and the application was not
  listening: the wait is now measured and written down. A crowded Wi-Fi channel,
  the likeliest cause, is also named now: the retry share was measured, logged
  and fed to the video buffer, but the only thing the link could ever report was
  being on 2.4 GHz, so a 5 GHz link losing four frames in ten said nothing.
- **A phone left connected and idle kept showing a stale warning.** As soon as
  one phone carried a window, only the phones carrying windows were asked about
  their heat, battery, storage and link; the others were neither refreshed nor
  dropped, since pruning only removes a phone that has gone away. Every
  connected phone is asked again. It costs four ADB calls a minute per idle
  phone, the readings being cached for a minute.
- **A launch that opened some windows said nothing about the ones it failed to
  open.** Three out of five counted as a success. The two paths nobody watches,
  the session resumed at startup and the window reopened after a drop, threw
  their report away entirely: silence there was indistinguishable from success.
  A launch report now always reaches the log, and the paths where someone is
  waiting still say it on screen as well.
- **A failed action vanished before it could be read, and a successful one
  erased a warning that was still true.** The banner was rebuilt whole on every
  sweep, every two to six seconds, from the health findings alone. A notice now
  carries the moment it was raised and answers for itself whether it is still
  true, so it survives a sweep and expires on its own. That also fixes notices
  freezing while the panel is hidden: their expiry used to run only from the
  sweep, so one raised just before hiding the panel was still there, word for
  word, hours later.
- **The simulated mouse stayed offered after the phone that needed it was
  unplugged.** The per-device input verdict said it was kept until the device
  disappeared, and nothing made that true.
- **A launch failure never reached the screen.** The error banner of the
  account list took its visibility from one property, its text from a second
  and its colour from a third, each written by a different path. A failed
  action wrote only the first, so the banner opened showing the health notice
  left over from the previous sweep, and the failure's own words were
  reachable by no path at all. The colour lied too: it came from the worst
  finding in the whole application, including those shown under a phone's own
  name and absent from the banner. All three now come from one decision, and
  the banner is coloured by the worst of the lines it actually shows.
- **A quest guide counted the wrong number of quests in an achievement.** The
  foot of the window showed the progression the site publishes, which counts
  the site's own achievement: it announced 10 / 12 under a list of two quests,
  because ten of those twelve are not in the catalogue. It now counts the
  achievement's quests as the list shows them, so that one reads 1 / 2, and a
  quest belonging to no achievement reads 1 / 1 instead of nothing. Walking to
  the next quest still works from either.
- **An account moved down the list on its own, and lost its name.** A stopped
  or paused Android profile answers an empty package list with a zero exit
  code, which reads exactly like a profile that answered and has no game. The
  account was removed on that basis, then created again on the next sweep after
  the last account of its phone, without the name you had given it. A profile
  is now only declared game-less when it was in a state to answer: started, not
  in quiet mode, and with an unfiltered enumeration that came back. An account
  whose game really is uninstalled still leaves the list as before.

- **A renamed account went back to its old name a few seconds later.** The
  list keeps the accounts it last discovered, and every setting written from a
  row throws that copy away so the next sweep cannot put the old value back.
  The rename was the one that did not, so two to six seconds later the row was
  handed the stale copy and quietly took its old name again. The name on disk
  was right the whole time, which is why it came back on its own fifteen to
  sixty seconds later. A rename that fails to save now says so, as the other
  settings already did.

- **A tab kept the old name after a rename.** The label was copied from the
  name the account carried when scrcpy started, and only the rename path wrote
  it again. Renaming an account whose window was open but not docked, then
  docking it, gave the tab the name from before the rename and nothing ever
  corrected it. A tab now takes its name from the settings at the moment it is
  created, as the window titles do, so no label depends on having been told
  about a rename.

- **Pressing Enter on a renamed account did nothing.** The field wrote when it
  lost focus, so a name was only applied by clicking somewhere else. Enter
  applies it now, and Escape puts the stored name back rather than leaving you
  to remember what it was.

- **The first launch on a phone sometimes did nothing and had to be clicked
  again.** Opening a window asks the phone once per run what it can encode, and
  that question starts a scrcpy server just as an opening does. The two were
  fired together and the opening lost, reporting that the connection with the
  phone had failed. The question now waits its turn in the same queue as the
  openings on that phone. Reproduced on a real device: two openings started
  together fail one out of two, spaced by a second and a half they both succeed.

- **The phone's sound never reached the PC.** scrcpy's default audio source
  forwards the whole output through an Android device whose music volume reads
  zero on the phones measured here, so what arrived was silence, and scrcpy
  reported no error because the capture had started. The sound is now taken from
  the playback instead, which also leaves the phone its own speaker. An
  application that opts out of capture, a video service for instance, stays
  silent on the PC and nothing can change that. Phones below Android 13 keep
  scrcpy's own source, which works there and which the new one would have
  replaced with silence.

- **The desktop shortcut opened a console on every launch.** It targeted the
  batch file that republishes before opening, and Windows has to open a console
  to interpret a batch file: the shortcut's "minimised" decides how that window
  shows, not whether it exists. It now targets a compiled launcher that has no
  console at all, republishes without a window and then opens the application.
  The guarantee that you always play the current build is kept, which targeting
  the binary directly would have lost.

- **Closing the game on a phone under Android 11 reopened its window by
  itself.** scrcpy cannot capture the sound there and says so with two errors at
  startup, then mirrors normally for as long as you like. Those two lines were
  counted as a refusal, and the session carried that verdict from its first
  second: closing the window by hand was read as a link that had dropped, and
  the window came back three times before the application gave up. A line about
  sound is no longer an end. A phone that goes away while playing announces
  itself on its own line, which still closes the session.

- **Both phones announced "Game not installed" several times a minute.** The
  accounts list invalidates its cache on ten ordinary gestures, Launch and
  Restart and Stop among them. On the next tick the display was handed an empty
  list, and an empty list was read as an answer: no phone carries the game. Both
  phones turned orange for the 2.9 seconds the real search takes, then snapped
  back.

  A phone now carries one of three verdicts instead of two booleans: it has the
  game, it has not, or we have not asked yet. A pass that does not know writes
  no verdict, and the last one established simply stays. "Not known yet" is no
  longer spellable as "absent", which is what made the lie possible.

- **Pairing a new phone could become impossible, and the message sent you the
  wrong way.** As soon as two phones announce themselves at once,
  `adb mdns services` gives a single address to every instance it lists, so one
  announcement carries the other device's address. The window then aimed at a
  port that answered nowhere and reported a possibly expired code, which sent
  the user to reopen the code screen on the phone. That draws a fresh port, so
  every retry made the next one worse. Measured on a Mi 9T Pro at `.23` and a
  13T Pro at `.16`, where pairing aimed at `.16` throughout. ADB returns the
  very same "protocol fault" for an unreachable address and for a refused code,
  so the address is now probed before the code is spent. The live addresses ADB
  already knows are tried on the same port, and failing that the "IP address and
  port" shown on the phone can be typed in. The code is kept in that case: it
  was never sent.

- **A phone whose address had changed stayed stuck on the old one.** The pairing
  window recognised an announcement by its mDNS name, which never changes, and
  kept the address and port first seen. A phone draws a fresh pairing port every
  time its code screen is reopened, and DHCP can move it meanwhile, so the
  window could aim at an address the phone had long left.

- **A phone could be wrongly reported as having lost its pairing key.** A
  refused connection counted as proof of that, and sent the user back to type a
  code. Since an announcement can carry another device's address, a silence is
  now nobody's fault: only a refusal from an address that answers is reported.

### Changed

- **The phone list no longer waits on the health readings.** Each phone is
  asked six questions in turn, thermal, battery, storage, Wi-Fi link and two
  more, measured at 2.2 seconds for two devices and growing with every phone
  added. The list used to wait on all of it before showing anything, although
  none of those answers say which phones are there. They are now gathered once
  the list is on screen, and the gauges and warnings fill in a moment later.
  Measured at launch: the list appeared 1.83 seconds sooner, and later sweeps
  are unaffected, the readings being cached for a minute.

- **Your phones appear before their accounts are looked for.** Finding the
  accounts asks two questions of every profile of every phone and was measured
  at 2.9 seconds, the longest thing a sweep does, and the list waited on all of
  it although the phones themselves were already known. They are shown as soon
  as discovery finds them, each saying it is looking for accounts, and the rows
  fill in when the search answers. Measured at launch: the phones appeared at
  1.12 seconds instead of 3.35. A phone plugged in later behaves the same way.
  While the search runs, a phone never claims the game is missing: the absence
  of a row means nothing until the search has answered, and saying otherwise
  would have put "Game not installed" under a phone that has it.

## [0.3.0] - 2026-09-12

### Added

- **A quest's end now says what it unlocks.** The guide site publishes a
  "Next quests and milestones" column at the foot of the page, sorted by
  objective, which the application was hiding in favour of its own window
  footer. That footer can only announce one continuation and falls silent as
  soon as the site names several: measured across the 782 guides, nearly four
  hundred quest endings said nothing at all. The column is now shown exactly as
  the site draws it.

### Changed

- **The success tree opens in the browser.** It is the only page of the site
  treated this way, and for a reason: it is not a page you read but a tool you
  unfold and explore. Until now it opened in an application window, correctly,
  but at the cost of one more step before the button that finally led to the
  browser.

- **Guide indexing now says what it is doing.** Only one of its five stages
  reported progress, and it was the shortest: the counter reached "782 / 782"
  within seconds, then sat frozen for four fifths of the time. All five are now
  named, the previous day's tree stays on screen during the rebuild instead of
  announcing zero, section pages are read four at a time, and the duration is
  finally recorded: **14.7 seconds** measured, against the fifty the project had
  been quoting from memory.

- **A simulated physical mouse, as a last resort.** It bypasses the Android
  input path that some manufacturer skins block. The checkbox appears only on a
  device where the refusal has actually been observed, it is off by default, and
  the key that gives the cursor back to the PC is written underneath it: the
  desktop loses its mouse for as long as a game window has focus.

- **The application says when a phone refuses clicks.** This is the quietest
  symptom in the field: the window shows the game and responds to nothing, with
  no error whatsoever. The probe that detects it already existed, but you had to
  go and open a help sheet to find it; it now runs once per device, the moment
  its first window opens.

- **"Needs pairing" instead of "Offline"** when the phone is present, reachable,
  and refusing this PC's key. The three wireless connection failures are written
  differently: a closed port and an absent machine carry a network error code, a
  refused key carries none, because the connection did succeed. The tooltip says
  what to do, and that turning wireless debugging off and on will not be enough.

- **A search indicator at startup.** The list stayed empty and silent while the
  application looked for remembered phones, which can take several seconds when
  one of them is switched off. It now says "Looking for phones...".

- **The application finally checks that the battery setup was done.** It had
  been explaining it for a long time without ever verifying it, although the
  phone answers in a single command. Of one user's two devices, only the one
  that does not disconnect had been set up.

- **The banner shows every finding, one per line**, most serious first, instead
  of the single worst one. One phone carried three at once, and you had to fix
  the first to learn the second existed.

- **Each phone's battery level, permanently.** A drawn gauge and the percentage,
  next to the device name, with a bolt when it is plugged in. The reading was
  already taken every minute and only the twenty percent alert came out of it;
  the level is now visible before launching five accounts, not once it is too
  late.

- **The phone's summary, before launching and not only during.** Battery and
  free space join temperature and Wi-Fi band, and the four speak with one voice,
  the most serious first. A plugged-in phone says nothing: it is not the level
  that worries, it is the level going down.

- **The month's event in the Almanax window**, read from the page already
  loaded.

- **A quality tier per account.** The main account at maximum, the mules at low:
  that much less processor, bandwidth, heat and battery. Each account follows
  the shared setting until it is given one of its own, and a new tier applies
  the next time its window opens.

- **A fluidity diagnostic**, off by default. It records in the log the frame
  rate each window actually receives, and the encoders the phone offers. Zero
  frames per second is not a fault: scrcpy only encodes what changes.

- **The hardware encoder is forced when the device would put a software one
  ahead of it**, and only in that case.

- **Save and restore your settings** to a file, to carry them to another PC. A
  restore plainly refuses a file written by a newer version, rather than
  silently losing part of it.

- **Time spent on each account this week**, on hovering its icon. Information
  only: no limit, no reminder.

- **Warnings fit on one line.** A triangle, yellow for what gets in the way and
  red for what will cut the session short, then a truncated line whose full text
  appears on hover. The message used to take two or three lines in the middle of
  the account list.

- **The display buffer is now per phone.** It was computed from the first
  device's link and applied to every window. With two phones on different bands,
  the one on 2.4 GHz received a 25 ms buffer instead of the 42 its link calls
  for.

- **A window that will only ever show the lock screen says so.** On an older
  phone, the virtual display follows the lock: the window opened on a clock and
  a padlock, and the launch announced "no problem".

- **An account's tier now also sets its frame rate.** It lowered resolution and
  bitrate, but an account on the low tier still ran at sixty frames.

### Fixed

- **The application appears four and a half times sooner.** It launched a full
  rediscovery of every profile on every phone before looking at whether there
  was anything to open at all: three seconds of empty screen to conclude that no
  instance was ticked, an answer the settings already held. The window now
  appears in 840 ms instead of 3,950.

- **Forgetting a device took two clicks, and lied in between.** The device
  stayed on screen after the first one, labelled "Game not installed" when its
  accounts had simply just been erased. The final refresh did not run while a
  scan was already in progress, which is the case nearly every time. The device
  now leaves the view immediately, and unpairing finally leaves a trace in the
  log.

- **The game left an empty card in the phone's running applications list.** The
  process was indeed stopped, but its card stayed at the top of the list,
  indistinguishable from a live application, and tapping it relaunched the game.
  The game is now launched outside recents when it lives on a virtual display,
  so the card is never created. Measured: process priority unchanged to the
  digit.

- **The game stayed open on the phone after its window was closed.** A session
  held the address the phone had when it opened, and wireless debugging changes
  port on every restart: the stop order went to an address the ADB server no
  longer knew. It now targets the current address, falling back to the one used
  at launch. The failure was also being swallowed under a false comment; the
  stop now says whether it succeeded, and a notice names the account whose game
  is still running.

- **The application crashed at startup when no window opened.** The rule "no
  window or panel left, so quit" fired during launch, closed the panel, and the
  rest of startup called Show on an already closed window. It no longer applies
  before startup has finished.

- **The reconnection notice stayed on screen after the phone left.** "The link
  dropped, its window is reopening" was displayed under an "offline" row, so
  under a device where no window will reopen at all. It now clears when the
  device is no longer reachable, or once the attempts have run out.

- **The application woke the phone twice as often as needed during play.** The
  lock check re-read the device every four seconds even once unlocked, and the
  network announcement scan ran every five seconds even when every phone was
  answering. Both space out when there is nothing left to watch: two thirds
  fewer commands for the phone, which is encoding during that time.

- **An offline device explains what to check.** "Offline" did not say whether
  the phone was switched off, on another network, its wireless debugging
  disabled, or whether it had forgotten this PC. The tooltip gives the list, in
  the order to look, and ends with the one you would not guess: the pairing is
  gone.

- **The button that forgets a device was called "Unpair"**, and carried a broken
  chain. It cancels nothing on the phone's side, it removes the device from this
  PC; the confirmation sentence already said so, and the button's name claimed
  the opposite. All the more confusing on a phone that is precisely no longer
  paired. It is now called "Forget device" and carries the bin, the same one
  already used to delete a profile.

- **A device that needs pairing says where to go.** A short line under its name,
  "Through 'Pair a new device', at the top.", and only where the refusal has
  been observed.

- **Startup waited for absent phones one after another.** Reconnections were
  sequential, on the grounds that ADB serialises connections anyway; measurement
  says otherwise, two connections to absent devices take 19.3 s together against
  22 s for a single one. They are now made in parallel, and the last known
  address has only five seconds to answer before the network scan takes over,
  instead of the twenty-two seconds the system grants a switched-off machine.

- **Each device's summary appears under its name**, and no longer in a banner at
  the foot of the list, where it seemed to be talking about the last device
  shown, which was precisely the one with nothing wrong. One finding per line,
  under the heading of the phone concerned, the full text on hover. The banner
  keeps what targets no device, and the findings of a device that has no row in
  the list, for want of a heading to put them under.

- **The device summary did not say which device it was talking about.** With two
  phones connected, "Android does not have this game in its list" left you to
  guess which of the two to fix. The name comes first as soon as there are
  several devices, as discovery warnings already do.

- **The input probe's verdict was drawn in black on a dark background.** Its
  style, written on the element itself, did not inherit the theme's implicit
  style: WPF replaces rather than extends when "BasedOn" is missing, and the
  text fell back to the default black. Only the refusal verdict was visible,
  because it alone set a colour. A test now guards the door, across the whole
  windows folder.

- **The padlock warning shouted when all was well, and fell silent when it
  mattered.** It looked only at the device's capability, never at the state of
  its lock: an unlocked phone, game on screen, was told a newer Android was
  required. And the message lived only two seconds, the next scan erasing it. It
  has moved into the device summary: it appears for as long as it is true,
  disappears on unlocking, and says what to do.

- **The language setting had no effect.** It was read, logged, and the
  application still displayed the Windows language: a culture set inside an
  async method reverts to its previous value as soon as the method yields.
  Measured, setting on "en", thirty milliseconds after it was set: "fr-FR". The
  English and Spanish translations were therefore unreachable from the
  interface.

- **Ten displayed strings were written in French in the code**, including
  "Connecting...", the name given to a new account, the three frame rates in the
  quality setting, the name of the main profile and the title of the "Add an
  account" dialog, which already had its translation key. A test now guards the
  door.

- **Three help texts were wrong.** DT Hub does not keep the phone's screen awake
  but the virtual display where the game runs; the high quality tier is capped
  at 1440 and not at the window's resolution; and the same tier was called
  "High" in the settings and "Max" in the per-account picker.

- **Spanish mixed familiar and formal address**, sometimes within a single
  sentence. Thirty-one keys return to the familiar form, which is what the rest
  of the file uses.

- **A dropped link no longer closes the application.** When the last game window
  died without being asked to, DT Hub closed with it. A Wi-Fi hiccup was enough,
  and disconnections are the first complaint of DOFUS Touch players. The window
  now reopens by itself, up to three times, spacing out the attempts, and the
  banner says so. A window closed by hand stays closed: scrcpy exits cleanly in
  that case, and with an error when the link drops.

- **A dropped link is finally recognised as one.** scrcpy announces it with a
  warning and not an error, and the application looked only at errors: the most
  frequent failure passed for a deliberate close.

## [0.2.0] - 2026-09-09

Vingt et une décisions depuis la 0.1.0. Le fil conducteur : rendre visibles les
pannes qui ne disaient rien.

### Ajouté

- **Tester la simulation d'entrée**, dans la fiche « La souris ne fait rien ».
  C'était le symptôme le plus fréquent du terrain et le seul entièrement muet :
  l'image passe, la fenêtre s'ouvre, le clic ne fait rien, et il n'y a aucune
  erreur parce qu'il n'y a aucune faute. ADB accepte d'afficher, pas d'injecter.
  Un bouton pose la question à l'appareil et rend l'un de trois verdicts, dont
  « je n'ai pas su dire » : se tromper de diagnostic coûterait plus cher que de
  n'en donner aucun.

- **Un avertissement quand l'appareil chauffe.** C'est la limite qui mord en
  premier sur une tablette à plusieurs comptes, et elle est silencieuse comme la
  précédente : rien n'échoue, tout ralentit. L'état thermique est lu une fois par
  minute, et seulement sur les appareils qui portent une fenêtre ouverte.

- **Le clavier physique simulé**, en option. Le clavier virtuel de certaines
  marques avale les caractères : la fenêtre répond à la souris et rien ne
  s'écrit. Décoché d'origine, parce qu'un clavier physique est lu selon la
  disposition réglée dans Android : mal réglée, un AZERTY tape en QWERTY.

- **Le bouton « Révoquer les autorisations de débogage USB »** dans la fiche de
  souris, avec le chemin de menu de la marque détectée.

- **Le logo de papycha.fr** sur le bouton des guides et dans la fenêtre de
  guides, avec leur accord.

- **Le bouton Profils dit quand un profil est actif.** Il fallait ouvrir la bulle
  pour savoir lequel servait.

- **Un lien vers la recherche du site**, en bas de la fenêtre des guides, dès
  qu'on cherche quelque chose. Notre catalogue ne connaît que des titres ; le
  site cherche dans le corps de ses articles, donc les objets, les monstres et
  les personnages. « ocre » n'y donne rien et lui en trouve deux cent huit. Le
  lien ouvre sa recherche dans votre navigateur.


### Modifié

- **L'application s'ouvre tout de suite.** Le panneau paraissait après le
  lancement des sessions, soit plusieurs secondes d'écran vide où l'on croyait
  qu'elle n'avait pas démarré.

- **Le débit se calcule au lieu d'être fixé par palier.** L'échelle était à
  l'envers : le palier maximal recevait 0,016 bit par pixel contre 0,090 pour le
  palier bas, et rendait donc une image plus grossière en mouvement, l'inverse de
  ce qu'il promet. Le débit suit maintenant la définition et la cadence
  réellement retenues.

- **Un tampon d'affichage adapté à la liaison.** Mesuré sur une liaison que rien
  ne saturait, la latence allait de 4 à 223 millisecondes. Le tampon échange
  cette irrégularité contre un retard constant, de 0 en USB à 60 au pire.

- **Le bridage selon la bande passante est retiré.** Il coûtait de la netteté
  sans rien gagner : la bande passante n'a jamais été le facteur limitant.

- **Un profil cloné est demandé en priorité**, et l'on prévient quand le
  téléphone n'en accorde pas : un profil professionnel amène trois cent
  cinquante-neuf applications et des icônes à valise, là où un cloné en amène
  vingt-deux.

- **Fermer une fenêtre arrête aussi le jeu sur le téléphone.** Un compte fermé
  gardait deux cent vingt mégaoctets et sa connexion aux serveurs, et les oubliés
  s'accumulaient d'un lancement à l'autre.

- **Le bloc d'état de la connexion ne paraît que s'il a quelque chose à dire.**
  Il annonçait « aucun téléphone connecté » quand rien n'était branché, ce qui
  est normal et n'a pas à s'afficher.

- **Les étapes d'un guide ne sont plus résumées.** Le bandeau et la liste des
  rangs portaient une ligne de prose tronquée alors que la page a le paragraphe
  entier juste au-dessus. Le rang suffit à s'y rendre. Ce qui reste est ce qui
  situe vraiment : le départ d'une quête, composé des métadonnées du site, et
  les titres de section d'une fiche de donjon, de raid, de tanière ou de chemin,
  désormais rendus tels quels. Le résumé leur ajoutait un point final :
  « Les salles » s'affichait « Les salles. »

- **L'Almanax du jour, dans DT Hub.** Une touche à côté de « Guides » ouvre une
  fenêtre qui dit ce qu'il faut apporter aujourd'hui, avec une bande de sept
  jours en tête pour préparer les suivants. L'information est lue sur le portail
  d'Ankama, filtré sur DOFUS Touch : le calendrier de DOFUS n'est pas le même, il
  demandait « 1 Aile de dragodinde » le 10 septembre 2026 quand Touch demandait
  « 1 Dent de Dragodinde ». La page n'est pas affichée telle quelle mais réduite
  à l'offrande, au bonus, à la quête et au Méryde. Rien n'est conservé après la
  session, et si le bloc lu n'est pas celui de DOFUS Touch, la fenêtre le dit au
  lieu d'afficher une offrande.

- **L'ordre des onglets va du concret au réglage** : Appareils, Fenêtres,
  Raccourcis.

### Corrigé

- **Rompre l'association rompt vraiment.** L'appareil restait affiché, revenait
  au balayage suivant et survivait à un redémarrage. Trois causes : le balayage
  le réinscrivait dans le geste même qui le retirait, aucune connexion ADB
  n'était coupée, et rien ne gardait mémoire de la rupture. Le registre retient
  maintenant les appareils écartés, et seule une nouvelle association lève la
  marque.

- **La fenêtre de jeu ne saute plus à l'ouverture.** Elle naissait onze pixels à
  gauche et quarante-cinq au-dessus de sa place, puis on la recalait sous les
  yeux : les coordonnées données à scrcpy visent la zone client, pas le cadre.

- **Un compte dont le jeu a été désinstallé disparaît de la liste.** Le nettoyage
  ne couvrait que la disparition du profil entier.

- **Les étapes des guides ne retiennent plus les phrases qui n'ordonnent rien.**
  Le garde ne regardait qu'un mot en arrière, si bien qu'un présent passait pour
  un impératif.

- **Le départ d'une quête n'est plus compté comme une étape.** Un guide qui ne
  porte qu'une consigne annonçait « Étape 1 / 2 » : la ligne de départ, qui dit
  où la quête se lance, entrait dans le total. Elle reste affichée et porte
  maintenant le mot « Départ » au lieu d'un rang. Sur les 782 guides du site,
  182 n'ont qu'une consigne et 30 n'en ont aucune.

- **« Suivant » ne saute plus une quête.** Dans une liste de quêtes d'un succès,
  l'ordre des prérequis l'emporte désormais sur la colonne du site.

- **La fenêtre d'association se ferme d'elle-même** quand la connexion arrive
  après qu'on a cessé de regarder.

- **Le bouton de lancement se débloque plus tôt**, et les autres se bloquent tout
  de suite : le verrou était pris trop tard et rendu trop tard.

- **Un profil créé mais vide est repris** au lieu d'être refusé.

- **L'application n'affirme plus une absence qu'elle n'a pas vérifiée.** Un profil
  qui refuse de répondre n'est plus compté comme un profil sans le jeu.

## [0.1.0] - 2026-09-03

Première version publiée.

### Ajouté

- Un rapport d'incident qu'on peut envoyer. La fenêtre qui s'affichait sur une
  faute montrait un chemin de dossier et un bouton « OK » : le message de
  l'erreur, qui dit ce qui s'est passé, n'atteignait jamais l'écran. Elle montre
  maintenant la faute, laisse lire le rapport avant de le copier, et ouvre le
  formulaire de signalement du dépôt. Un bouton « Signaler un problème » fait la
  même chose sans attendre une faute, dans l'onglet Fenêtres.

  Le rapport porte la version, le système, les écrans, l'erreur avec sa pile et
  les lignes utiles du journal de la session. **Rien n'est envoyé** : il va dans
  le presse-papiers, et c'est la personne qui décide. C'est la ligne que suit
  déjà le signalement vers papycha.fr.

  Ce qui identifie en est retiré : adresses et ports, noms de débogage sans fil
  qui portent le numéro de série, valeurs collées à « --serial= », noms de
  périphériques d'écran, le nom du compte Windows dans les chemins, et les noms
  que la personne a choisis pour ses comptes et ses profils de lancement. Mesuré
  sur sept fichiers de journal réels, près de trois lignes sur dix en portaient
  une, quand les erreurs en font sept sur cent.

  Le rapport est donc collable en public, sur un salon Discord comme ailleurs.
  Vérifié sur un vrai rapport : ni numéro de série, ni adresse, ni nom de
  compte, ni nom de profil, ni nom d'écran, ni nom d'utilisateur Windows.

  Chaque ligne de journal porte désormais l'identifiant du lancement : quatre
  cent huit démarrages en six jours se mêlaient dans sept fichiers, et un
  rapport aurait emporté les fautes de la veille.

- Une fenêtre de préparation au premier lancement. DT Hub téléchargeait
  dix-neuf mégaoctets d'outils avant d'afficher un seul pixel, avec un délai
  réseau de dix minutes : sur une ligne lente, l'exécutable semblait mort. La
  fenêtre nomme ce qui manque, l'adresse d'où ça vient, et montre le
  téléchargement puis la vérification de l'empreinte. Elle ne paraît que s'il
  manque quelque chose, donc au premier lancement seulement, et se ferme d'elle
  même. Mesuré : elle s'affiche à sept cent cinquante millisecondes.

  Onze de ces dix-neuf mégaoctets ne servaient à rien : le ménage des fenêtres
  restées d'une exécution précédente réclamait scrcpy pour les reconnaître,
  alors qu'un scrcpy jamais installé n'a jamais pu en laisser.

### Modifié

- Le démarrage efface ce que les versions précédentes ont laissé dans les
  fichiers temporaires. Windows y dépose les bibliothèques graphiques dont un
  programme d'un seul fichier a besoin, dans un dossier neuf à chaque version :
  cent soixante et un dossiers, un giga-octet et trois cents mégaoctets sur le
  poste de développement. Le fichier lu à ce sujet dans le README disait le
  contraire, il dit maintenant ce qui est.

- Le message affiché quand le démarrage échoue dit enfin de quoi. Un dossier de
  données impossible à créer ne laisse ni journal ni fenêtre de signalement, et
  la boîte du système n'affichait que « Le démarrage a échoué ». Elle porte
  maintenant la cause, qui nomme le chemin en défaut.

- Les options de publication tiennent dans un seul fichier au lieu d'être
  recopiées dans trois, et la chaîne de livraison vérifie que le binaire est
  bien autonome : elle ne lisait que quatre chaînes de métadonnées Windows, qu'un
  exécutable de cent cinquante kilooctets réclamant le .NET installé porte à
  l'identique.

- Le panneau de réglages s'ouvre moins haut : 520 unités au lieu de 580. C'est
  la hauteur exacte de l'onglet Raccourcis, le plus long des trois à ne pas
  défiler, et les deux autres défilaient déjà. La fenêtre reste redimensionnable
  et retient la taille qu'on lui donne.

- Les écrans dessinés dans les aides ressemblent enfin à ce qu'ils montrent. Ils
  portent une barre d'état, une flèche de retour et un grand titre comme les
  réglages d'Android depuis leur douzième version, et leurs lignes ont chacune
  son icône et son chevron dans une carte arrondie. C'étaient auparavant des
  barres grises sous un mince bandeau de titre, qui n'évoquaient aucun téléphone
  en particulier.

  Chaque ligne porte une pastille de couleur, comme Android range ses réglages,
  et une ligne sur trois annonce son état sous son nom. Un écran porte un
  interrupteur, un seul : un écran par lequel on ne fait que passer n'en porte
  pas quatre.

  La longueur des barres est inégale, sans quoi cinq intitulés de même taille
  trahissaient le dessin, mais rien ne bouge d'une ouverture à l'autre : le même
  écran se rend toujours pareil, teintes et interrupteur compris. Cinq lignes par
  écran au lieu de quatre.

  Ce qui n'est pas dit reste muet : ni les autres intitulés du menu, ni l'heure,
  ni le niveau de batterie ne sont inventés. Les pastilles ne disent pas la
  couleur d'un réglage en particulier, que la fiche de marque ne donne pas, mais
  qu'il y en a une.

- Fermer le cadre à onglets ferme les comptes qu'il logeait. Ils en ressortaient
  libres, et l'on se retrouvait avec autant de fenêtres de jeu éparses qu'on
  croyait venir de fermer : le geste ne faisait pas ce qu'il annonce. Chaque
  compte se comporte comme si l'on avait fermé sa fenêtre, sa place est retenue
  et il ne rouvrira pas de lui-même au prochain démarrage. Il reste logé en
  onglets, et y retournera le jour où on le rouvre.

  Les fenêtres quittent le cadre avant de se fermer, et masquées : une fenêtre
  logée est fille du cadre, et Windows détruirait ses enfants avec lui sans
  laisser à scrcpy le temps de s'arrêter proprement.

- Une quête seule peut désormais se glisser entre deux quêtes d'un succès, et
  la série reprend après elle sous un intertitre marqué « suite ». Au Château
  d'Amakna, « Étre plus royaliste que le roi » réclame neuf quêtes seules au
  milieu de sa propre suite : traité comme un bloc insécable, il les rejetait
  toutes avant ou toutes après, et la liste montrait des quêtes avant ce
  qu'elles exigent.

  Deux succès, eux, ne s'entrelacent jamais. Les laisser faire lèverait les
  onze fautes restantes, mais l'île de Frigost, où huit succès se réclament
  mutuellement, devenait un va-et-vient de quinze intertitres entre les mêmes
  séries.

  Mesuré : les prérequis placés après la quête qui les réclame passent de onze
  à sept, et quatre succès sur cent soixante et un sont coupés.

- La barre du bas se lit en deux temps. « Guides » est seul à gauche, dans son
  propre cadre : il ne range rien, contrairement aux deux autres, et les mettre
  tous les trois dans le même cadre les donnait pour trois gestes de même
  nature. Empiler et côte à côte gardent leur cadre commun et perdent leur
  texte : leur dessin dit ce qu'elles font, et le pied de fenêtre respire.

- Empiler et côte à côte ne paraissent plus qu'avec de quoi ranger : deux
  fenêtres au moins, et que ces placements peuvent bouger. Une seule fenêtre
  ouverte, ou deux logées dans le cadre à onglets, et les presser ne faisait
  rien. Elles se retirent maintenant plutôt que de ne rien faire, et
  reparaissent dès qu'une deuxième fenêtre libre s'ouvre.

### Corrigé

- La liste d'une zone montrait des quêtes avant ce qu'elles exigent. Dans
  « Un Piou, c'est tout ! », « L'île Céleste » paraissait avant « Le voyage vers
  Incarnam », qu'elle réclame : la carte des rangs, calculée à l'indexation, se
  contredisait elle-même sur treize quêtes. Les quêtes d'un succès se rangent
  maintenant par leurs prérequis d'abord, la carte ne servant qu'à départager
  celles qu'aucun prérequis ne sépare.

  Une quête qui réclame son propre succès le réclame en entier : elle passe donc
  après tout le reste de son bloc. « En route pour Plantala » est seule dans ce
  cas.

- Une quête seule qui découle d'un succès était rangée après **tous** les
  succès, et non derrière le sien. « La découverte d'un vaste monde », dont le
  seul prérequis est le succès « Devenir une légende », se retrouvait trente
  rangs plus bas à Astrub, entre des quêtes qui n'ont rien à voir. Elle prend
  désormais le rang du succès dont elle découle, et le suit immédiatement ; une
  suite de quêtes seules le suit tout entière, dans l'ordre où on l'enchaîne.

  Mesuré sur les vingt-cinq listes : les prérequis placés après la quête qui les
  réclame passent de treize à onze. Les onze qui restent viennent de boucles,
  deux blocs se réclamant l'un l'autre, qu'un succès insécable ne peut pas
  départager.

- La quête suivante manquait à la fin d'une série. « La légende du Chevalier de
  l'Automne » clôt le succès « Un nouveau départ » et le site propose en pied
  d'article « Dans les pas du Chevalier de l'Automne », première quête du succès
  suivant ; le bouton, lui, disparaissait.

  Deux causes. Le site écrit ses prérequis de trois formes, et l'application
  n'en lisait qu'une : soixante-treize libellés sur cinq cent quatre-vingt-quatre
  nommaient un jalon ou un succès entier plutôt qu'une quête, et l'arête ne
  reliait rien. Et le bloc de progression que le site publie en pied d'article,
  celui qui donne la quête précédente et la suivante, était analysé puis jeté.

  Le site fait foi désormais : quand il nomme une voisine, et une seule, c'est
  elle. Mesuré sur les sept cent quatre-vingt-deux guides : huit succès de moins
  s'achèvent sur un cul-de-sac, quatre suivantes et deux précédentes
  apparaissent, quarante-huit suivantes et quatre-vingt-dix-neuf précédentes
  suivent maintenant l'ordre du site plutôt que le nôtre. Des quatre-vingt-un
  succès qui restent sans suite, soixante-dix-sept sont muets sur le site lui-même.

- Le bloc de progression du site était masqué dans la fenêtre des pages liées,
  qui n'a pas de pied de fenêtre pour le remplacer : l'information y était perdue
  sans contrepartie. Il n'est plus masqué que dans la fenêtre des guides.

### Ajouté

- L'application se montre en anglais, en français ou en espagnol. Elle suit la
  langue d'affichage de Windows sans qu'on ait rien à régler, et retombe sur
  l'anglais quand cette langue n'est pas traduite. Un choix dans l'onglet
  Fenêtres permet de la contredire ; il prend effet au démarrage suivant, les
  fenêtres lisant leurs textes une fois pour toutes à leur construction.

  Toutes les fenêtres sont traduites, infobulles comprises, ainsi que les
  messages d'erreur, les noms de touches et les phrases bâties à l'exécution.

  Ce qui reste français y reste à dessein : les guides viennent de papycha.fr
  et sont du contenu, non de l'habillage ; les noms de rubriques et de quêtes
  servent de clés d'appariement avec le site ; les chemins de menus Android
  sont ceux du téléphone, et l'aide le dit déjà en toutes lettres.

### Corrigé

- L'ordre des onglets rangé à la souris ne survivait pas au démarrage suivant
  quand un profil s'ouvrait tout seul : le profil rejouait l'ordre qu'il avait
  retenu. L'ordre des comptes est un réglage général, celui de la liste comme
  celui des onglets ; un profil le retient pour être lisible, il ne l'impose
  plus. Il tient donc pour la session, pour les profils et d'un lancement à
  l'autre.

  Les onglets sont par ailleurs remis dans cet ordre à chaque arrivée, et non
  laissés dans celui des arrivées : les afficheurs ne se préparent pas à la
  même vitesse.

- Suivre un lien du guide vers un chemin, un donjon, un raid ou une tanière
  perdait le fil : la flèche de retour ne paraissait pas, et rouvrir le panneau
  montrait la branche de ce qu'on venait d'ouvrir au lieu de la fiche d'où l'on
  venait. L'historique n'était empilé que dans la branche des quêtes, et il ne
  savait retenir que des quêtes.

  La règle est maintenant la même pour toutes les natures : un lien s'ouvre à
  part quand le catalogue ne le connaît pas, sur place quand il le connaît, et
  dès qu'il s'ouvre sur place la flèche paraît et le panneau garde la fiche
  d'origine. Choisir dans la liste efface la piste : on a désigné où aller.

- Le repli des boutons « précédente » et « suivante », quand la page n'est pas
  au catalogue, laissait l'état sur la page d'avant : le panneau rouvrait sur
  l'ancienne rubrique et le signalement nommait l'ancienne quête.

- Une adresse du catalogue ornée d'une ancre, « …/quete-x/#etape-3 », partait en
  fenêtre annexe alors qu'elle devait s'ouvrir sur place.

- Le formulaire de signalement se soulignait dès qu'on cliquait dans un champ.
  Le thème du site porte `label:focus { text-decoration: underline }`, et ses
  champs sont écrits `<label><span>intitulé</span><input></label>` : la
  décoration se propageait donc à l'intitulé et à la valeur. Elle est coupée là
  où elle naît, sur le libellé, une décoration ne s'annulant pas depuis ses
  descendants.

- Le bloc « Remonter une erreur » ne se repliait plus. Son résumé était masqué,
  en croyant qu'il faisait doublon avec le titre de la fenêtre ; c'était le seul
  moyen d'ouvrir et de refermer le bloc. Il est de retour, ramené à gauche.

- Les trois bulles de l'application demandaient deux clics pour rouvrir après
  s'être refermées d'elles-mêmes : la liste des étapes, celle des profils et
  celle des réglages fins. Leur ouverture ne suivait la bascule que dans un
  sens, si bien qu'une bulle refermée au clic ailleurs laissait la bascule
  cochée.

- La liste rouvrait sur la mauvaise rubrique. Une quête sur cinq appartient à
  plusieurs rubriques, et le catalogue n'en retient qu'une, la moins peuplée :
  rouvrir la liste depuis une quête de Frigost basculait sur « Quêtes
  principales ». Relevé sur le catalogue, 215 quêtes sur 782 sont dans ce cas,
  dont 93 qu'une rubrique transverse emporte et 80 dans l'autre sens. La liste
  garde désormais la rubrique d'où l'on vient quand la quête y figure, et
  l'étiquette de série des liens précédente et suivante suit la même règle.

- L'encart « Type : Principale » tenait lieu de première étape dans la fenêtre
  des guides. Il n'était pas compté comme étape, mais l'étape de départ est
  ancrée en haut de l'article, et cet encart était le seul morceau du bandeau
  d'intro que la fenêtre ne masquait pas : c'est donc lui qu'on voyait. Le
  bandeau y passe maintenant en entier, ses quatre blocs étant repris par le
  nôtre.

- Trois familles de paragraphes étaient comptées comme étapes sans en être.
  Relevé sur les 782 guides du site, quarante et une étapes sur 3 293 :

  - **L'annonce du départ écrite en prose**, « La quête se lance en [2,-16] en
    parlant à Kerubim Crépin », que le bandeau donne déjà en première étape.
    Trente cas. Elle est gardée quand elle porte en plus sa propre consigne.
  - **Le récit au présent**, « vous vous faites agresser par x2 Bandit ». Le
    garde qui distingue « vous partez » de « partez » ne valait pas pour les
    six verbes irréguliers. Sept cas.
  - **Les encarts « Important : »**, de même nature que « Attention : » et
    « Prérequis : » que la règle écartait déjà. Quatre cas.

  Chacune des quarante et une a été relue. Aucun guide ne perd sa dernière
  étape, et aucune étape n'apparaît là où il n'y en avait pas.

- Le bouton « Ajouter un compte » créait un compte inutilisable. Il faisait un
  utilisateur Android complet, et un utilisateur complet ne peut pas porter de
  fenêtre pendant qu'un autre compte est au premier plan : mesuré sur un Xiaomi
  23078PND5G sous Android 16, `cmd user is-user-visible` rend faux, et
  `am start` répond pourtant `Status: ok` avant de pendre soixante-dix secondes
  sans rien afficher. Le bouton crée désormais un profil rattaché au compte
  principal, dont la fenêtre s'ouvre en quatre secondes, vérifié jusqu'à
  l'écran de connexion du jeu.

- Un refus du téléphone était annoncé comme « l'application n'est plus
  installée », ce qui envoyait réinstaller un jeu bien présent. C'est ce que
  rendent le Dossier sécurisé de Samsung et les profils tenus par une politique
  d'entreprise. Un échec non reconnu reste maintenant sans interprétation, et un
  refus de permission est nommé pour ce qu'il est.

- Un profil en pause n'était pas vu. C'est l'interrupteur du profil
  professionnel, et la fonction principale de Shelter et d'Island : le lancement
  échouait sans que rien n'explique pourquoi. Le drapeau est désormais lu, le
  profil écarté avant le lancement, et le message dit de le rallumer sur le
  téléphone. Aucune commande ADB ne permet de le faire à sa place.

- Le démarrage d'un profil ignorait son propre résultat et lançait quand même.
  L'échec se manifestait plus loin, sous une forme que personne ne rattachait au
  profil.

### Ajouté

- Un bouton **Signaler une erreur**, dans le pied de la fenêtre des guides. Il
  ouvre le formulaire de signalement du site sur la page qu'on lit, dans une
  fenêtre à part, avec le champ « Où se trouve l'erreur ? » déjà rempli : la
  zone et la quête, c'est-à-dire ce que le site nomme lui-même.

  Rien n'est envoyé et rien d'autre n'est écrit : la description reste vide,
  c'est ce que le lecteur a vu, et c'est lui qui appuie. Le repère peut être
  effacé, et il n'écrase jamais une saisie en cours.

  La fenêtre ne montre que le formulaire : le reste de l'article y est masqué,
  le fond photographique du site remplacé par le nôtre, et les champs ramenés à
  la largeur disponible. La zone de texte ne s'étire plus à la poignée, sa
  hauteur suit celle de la fenêtre, et le formulaire défile.

  Le bouton d'envoi du site est rendu visible : sa feuille de style lui donne
  « background: currentColor » avec « color: Canvas », si bien que son fond
  prend la couleur de son propre texte et qu'il disparaît.

  Seuls les articles portent ce formulaire, quêtes, donjons et chemins compris ;
  les rubriques n'en ont pas et le site n'en a pas de général. Depuis une
  rubrique, le bouton mène donc à la page de contact du site, qui renvoie vers
  le serveur Discord de l'équipe.

- **Choisir une étape dans une liste**, en dépliant son rang dans le bandeau des
  guides. Les deux flèches n'avancent que d'une étape à la fois : revenir à la
  troisième d'un guide qui en compte treize demandait neuf clics, et rien ne
  disait ce qu'on trouverait en chemin. Chaque entrée porte son numéro et ce
  qu'il y a à y faire, celle où l'on est se distingue, et la page s'y replace.

- Un mode onglets : une icône sur la ligne du compte le loge dans un cadre
  unique, comme un onglet de navigateur, et un second clic lui rend sa fenêtre
  libre. Autant de comptes qu'on veut, un seul visible à la fois, et passer de
  l'un à l'autre est immédiat : les fenêtres sont cachées, non fermées.

  Les onglets se glissent pour changer leur ordre, qui est aussi celui de la
  liste des comptes : un seul ordre partout. Un trait bleu montre où l'onglet se
  posera, à gauche ou à droite de celui qu'on survole, et lâcher après le dernier
  range en fin de liste. L'onglet part à l'instant où on lâche, sans attendre le
  trajet par les réglages.

  Le cadre s'attrape par n'importe quel bord, comme une fenêtre de jeu libre :
  tirer un côté commande la hauteur, tirer le haut ou le bas commande la
  largeur, et le bord opposé ne bouge pas.

  Le cadre prend la forme de l'image du jeu et la garde à chaque
  redimensionnement, si bien qu'aucune bande noire ne subsiste sur les côtés :
  scrcpy verrouille le rapport de ce qu'il rend, et une zone d'accueil d'une
  autre forme lui laissait forcément du noir. S'il n'y a plus de place en
  hauteur, c'est la largeur qui cède.

  La fenêtre n'est ni recréée ni rouverte, seulement logée : basculer ne coûte
  ni les secondes d'une ouverture ni un nouvel afficheur virtuel sur le
  téléphone. Un compte logé échappe aux placements automatiques, qui
  lutteraient contre le cadre. Le cadre se referme quand son dernier onglet le
  quitte, et un onglet part avec la session qu'il montre : le laisser faisait
  croire que le compte était encore logé, et le rouvrir le sortait du cadre.

- Des profils de lancement : un ensemble de comptes **avec leurs positions et
  leurs réglages**, qu'on retient sous un nom et qu'on rouvre d'un geste.
  « Solo donjon » ouvre un compte en grand et en qualité haute, « Duo pêche »
  deux fenêtres côte à côte en qualité moyenne. Un profil emporte la position et
  la taille de chaque fenêtre, la qualité et sa personnalisation, la distance
  dans le jeu, l'ancrage et la taille en pourcentage, le son du jeu renvoyé sur
  le PC, le presse-papiers partagé, quels comptes sont en onglets et où était le
  cadre qui les logeait.

  Un profil peut être désigné pour le démarrage ; sans désignation, l'application
  rouvre ce qui était ouvert, comme avant. L'ouvrir ferme les fenêtres qui n'en
  font pas partie et ouvre celles qui manquent, après confirmation.

  Ils vivent derrière un bouton « Profils », sur la ligne du bouton
  d'association : dépliés dans la page, ils prenaient quarante-sept pixels à la
  liste des comptes, qui en manque dans une fenêtre courte. La bulle les montre
  tous, une ligne chacun, avec à droite de quoi l'ouvrir, le désigner pour le
  démarrage et le supprimer, cette dernière après confirmation. Désigner une
  ligne ne fait plus rien par soi-même : ouvrir est un geste à part, faute de
  quoi un simple clic d'exploration fermait des fenêtres de jeu en cours.

  « Créer un profil » annonce ce qu'il va retenir avant de demander un nom, avec
  les valeurs du moment plutôt qu'une liste figée : le nombre de comptes
  ouverts, la qualité, la distance dans le jeu et lesquels sont en onglets.

  Un compte retiré du téléphone depuis l'enregistrement est simplement ignoré :
  le profil garde sa raison d'être et les autres comptes s'ouvrent. Un profil
  enregistré avant que les profils ne portent les positions ouvre encore ses
  comptes, là où ils étaient.

- Un quatrième palier de qualité, « Personnalisé ». Ses réglages fins,
  définition maximale, cadence, finesse d'image et codec vidéo, s'ouvrent dans
  une bulle par le rouage qui paraît à côté de lui : dépliés dans la carte, ils
  lui faisaient gagner deux cents pixels de haut et repoussaient tout le reste
  du panneau. La hauteur ne bouge donc plus selon le palier choisi.

  La finesse se règle en bits par pixel, et non en mégabits comme le proposent
  les interfaces qui ne pilotent qu'un seul miroir. Ici la définition de
  l'afficheur suit la taille de la fenêtre : un débit absolu servirait
  grassement une petite fenêtre et affamerait une grande, ce que le reste du
  code avait précisément appris à ne plus faire. La définition, elle, est
  annoncée pour ce qu'elle est, un plafond.

- Le palier « Maximale » s'appelle désormais « Haute », et son rouage de
  réglages fins se tient sur la ligne du titre plutôt que sous les paliers, où
  il retombait seul à la ligne.

- Le nom du jeu paraît là où il lève une ambiguïté, et pas ailleurs : dans
  l'en-tête, sur les fenêtres « de jeu », et au-dessus de la liste des comptes.
  Il ne paraît pas dans la section « Sur le téléphone » : le son capté est celui
  de l'appareil entier et non celui du jeu, l'écran est celui du téléphone, les
  animations sont celles d'Android. L'y nommer aurait été faux.

- Une troisième ligne dit la définition réellement demandée, et prévient quand
  le plafond choisi n'y change rien : « vos fenêtres tournent en 2560 × 1440,
  au-delà ce réglage ne change rien ». Le palier retenu est le premier au-dessus
  de la fenêtre, donc monter le plafond plus haut que les fenêtres ne demande
  rien de plus, alors que l'interface laissait croire l'inverse.

- Sous ces quatre réglages, deux lignes qui disent ce qu'ils valent :
  « 0,090 bit par pixel et par image, confortable » puis « au plus 11,2 Mb/s
  par fenêtre, 22,4 Mb/s à 2 comptes ». La première est la mesure que l'encodeur
  reçoit vraiment, et celle dont l'absence avait laissé passer un débit à
  l'envers dans les paliers automatiques ; le codec y entre, H.265 rendant
  davantage à débit égal. La seconde compte les fenêtres ouvertes sur le
  téléphone, parce qu'elles partagent une seule liaison et un seul encodeur.

- Deux codecs au choix, H.264 et H.265. AV1 et VP8 ne sont pas proposés : relevé
  par `scrcpy --list-encoders`, un téléphone ordinaire n'a pour eux qu'un
  encodeur logiciel, qui coûterait bien plus qu'il ne rend.

- Le son du téléphone se renvoie sur le PC. C'est celui de l'appareil entier,
  Android ne sachant pas l'isoler par application : une seule fenêtre par
  téléphone le porte, sinon le même flux reviendrait en plusieurs exemplaires.

- Chaque compte porte l'icône du jeu, celle qui est sur le téléphone. Elle est
  tirée d'une seule entrée de l'archive de l'application, cinquante et un
  kilooctets pour une archive de quatorze mégaoctets qui ne bouge pas, puis
  gardée dans le cache. Une application dont l'icône n'est pas extractible
  n'affiche rien, et la liste reste ce qu'elle était.
- Un bouton ajoute un compte sur le téléphone, à droite de son nom. Il crée un
  profil Android, y installe le jeu et le démarre : le compte apparaît dans la
  liste, prêt à ouvrir, sans redémarrer l'application. C'est le mécanisme des
  comptes multiples d'Android, celui que la surcouche du téléphone emploie
  elle-même : rien n'est recopié, l'application reste celle de l'éditeur,
  signée par lui. Le profil naît vide, et le jeu y redemandera ses ressources
  et la connexion, ce que la confirmation annonce.
- L'aide « Plusieurs comptes sur un même appareil ? » disparaît : le bouton fait
  ce qu'elle expliquait. Ses fiches de marque servent encore, mais seulement
  quand le téléphone refuse la création, pour dire où aller à la main.
- Un compte dont le profil a été supprimé sur le téléphone quitte la liste tout
  seul. Il y restait indéfiniment, sans qu'aucun bouton puisse l'en retirer.
- La branche « Donjons » s'ouvre : les 83 donjons du site, rangés par palier de
  cinquante niveaux, avec leur niveau entre parenthèses et, à droite, la clef
  exigée, la taille de la pierre d'âme et la position. Le nom de la clef vient
  au survol.
- Une page de donjon se parcourt par ses sections - Monstres, Liste des salles,
  Boss, Mécanique du donjon, Les succès, Fin du donjon - et non par un résumé de
  paragraphe : ce n'est pas une suite de consignes mais un dossier.
- La recherche rend un quatrième groupe, « Donjons ».
- Deux sections de plus à la racine : « Raids » et « Tanières », avec leur
  niveau entre parenthèses et le classement par niveau. Le site ne le met dans
  ses métadonnées que pour une des dix : les neuf autres l'écrivent en clair
  dans leur première ligne, où il est désormais lu.
- Les chemins entrent dans la fenêtre, rangés dans la branche qu'ils servent :
  six sous « Donjons », quinze sous « Zone de Quêtes », chacun dans une
  sous-branche « Chemins ». Un chemin va aux donjons s'il écrit le mot
  « donjon » ou s'il partage au moins deux mots distinctifs avec un donjon du
  catalogue ; un seul mot commun ne suffit pas, faute de quoi le zaap de la
  canopée passerait pour le chemin de la Canopée du Kimbo.
- Un chemin se parcourt par ses étapes, « Jusqu'à la première grotte » à « Fin
  du chemin » : la règle qui distingue le dossier de la consigne ne regarde plus
  le bloc des donjons mais la présence de titres de sections.
- Les quêtes qu'aucun succès ne réclame ne sont plus rejetées en fin de liste.
  Elles se rangent à leur place dans la progression, suivant leurs prérequis :
  « Une arrivée mouvementée » ouvre désormais Albuera devant « Médiation
  expéditive », « En route pour Feudala » se glisse entre « Sous le bois de sa
  colère » et « Sous des nuages de cendre ». L'intertitre « Hors succès »
  disparaît.
- Les quêtes d'un succès se décalent et se relient par un filet vertical. C'est
  ce qui dit maintenant l'appartenance : l'intertitre et ses quêtes étaient au
  même retrait, et une quête au ras de la marge se reconnaît sans rien avoir à
  apprendre.
- Une quête que rien ne lie, ni prérequis reconnu ni quête qui la réclame, reste
  en fin de liste au lieu de se glisser au hasard entre deux succès. Au Château
  d'Amakna, « On recherche Ali Grothor » se retrouvait ainsi entre « Le vallon du
  château » et « Étre plus royaliste que le roi », sans rapport avec ni l'un ni
  l'autre. Cent deux quêtes seules sur trois cent une sont dans ce cas.
- La sentinelle des guides interroge les cinq catégories qu'on lit, et non plus
  le site entier. Sa date de dernière modification bougeait dès qu'un seul de ses
  mille douze articles était touché, même un dont on ne lit rien : une virgule
  ailleurs coûtait cinquante secondes de relecture. Cinq demandes de quarante
  octets, deux cents en tout, et les catégories bougent chacune à leur rythme :
  les raids n'ont pas changé depuis le 28 août, les tanières depuis le 18.
- Le moteur de rendu s'endort quand les guides se masquent, et se réveille quand
  ils reviennent. Mesuré : quarante-quatre mégaoctets rendus sur quatre cent
  soixante-trois. Les six processus restent, seule leur mémoire de travail se
  relâche.
- L'arrêt de l'application laisse tourner sa boucle de messages pendant qu'il
  range. WPF coupe le répartiteur dès que la méthode d'arrêt rend la main, et un
  « await » la lui rend : tout ce qui suivait, dont la pose de la mise à jour,
  se serait perdu dès qu'une fenêtre de jeu aurait été ouverte. Mesuré à la
  sonde, la suite s'exécute aujourd'hui, mais seulement parce que fermer zéro
  fenêtre se termine d'un trait. L'attente est bornée à huit secondes.
- Le guide rouvre à l'étape où on l'avait laissé, et plus à la première. Elle
  n'est reprise que si la page en compte encore autant : le site peut l'avoir
  raccourcie depuis.
- Une chaîne de contrôle compile et éprouve à chaque poussée, avertissements
  traités en erreurs. La seule chaîne existante ne se déclenchait que sur une
  étiquette de version : entre deux livraisons, un test rouge ne se voyait que
  sur la machine de celui qui avait écrit le code.
- La carte d'un donjon ne recouvre plus l'en-tête de la page. Elle remonte de
  quatre-vingt-quatorze pixels pour se glisser à côté, et recouvrait le niveau et
  la pierre d'âme dès que la fenêtre passait huit cent quatre-vingts pixels de
  contenu. Ce n'est pas notre mise en page : le site n'atteint jamais cette
  branche, sa colonne de guide faisant six cent cinquante pixels, un volet
  latéral prenant le reste. Nous écartons ce volet, donc nous y tombons.
- L'exécutable porte enfin son nom. Ses propriétés annonçaient « DT Touch »,
  resté d'un renommage, quand la fenêtre dit « DT Hub », et sa description était
  le nom du fichier. C'est ce que Windows montre et ce que SmartScreen cite. La
  chaîne de livraison refuse désormais un binaire dont le nom, la description ou
  l'éditeur manquent, et sait le signer dès qu'un certificat est configuré.
- L'application n'écrit plus rien à côté de son exécutable. Le moteur de rendu y
  posait son cache, faute qu'on lui dise où aller : vingt-quatre mégaoctets après
  une seule session, trois cent quatre-vingt-dix-neuf après quelques semaines. Il
  écrit maintenant sous `%LOCALAPPDATA%\DtHub\webview`, avec un cache borné à
  cent mégaoctets et balayé s'il déborde.
- La publication ne rend plus qu'un fichier. Trois fichiers de symboles et trois
  de documentation d'un paquet tiers traînaient à côté ; les symboles sont
  désormais embarqués, ce qui garde les numéros de ligne dans les journaux.
- L'application pose un raccourci dans le menu Démarrer, sur l'exécutable là où
  il se trouve. Elle ne se copie ni ne se déplace ; déplacer le fichier corrige
  le raccourci au démarrage suivant. Rien n'est fait depuis un arbre de sources.
- Le composant WebView2 absent se dit en clair, avec le lien pour l'installer, au
  lieu d'une fenêtre vide et d'une ligne de journal. C'est la seule dépendance
  externe de l'application.
- Les guides suivent le site au lieu de suivre un calendrier. L'application lui
  demande s'il a bougé, ce qui coûte quatre-vingt-dix-sept octets, et ne le relit
  que s'il a bougé. Une quête parue le matin était vue jusqu'à sept jours plus
  tard ; elle l'est le jour même, et pour dix-huit mégaoctets de moins quand rien
  ne change.
- Une relecture dit ce qu'elle a rapporté, « 3 quêtes de plus », sans qu'on
  l'ait demandée : personne ne demande une relecture, la sentinelle décide, et
  l'on veut savoir ce qu'elle a trouvé. Elle se tait quand le catalogue n'a rien
  gagné ni perdu, ce qui est le cas courant, le site remaniant souvent ses pages
  sans en ajouter.
- Une sonde de développement vérifie que le site se lit encore comme
  l'application le suppose : titres des donjons, sommaires des raids et des
  tanières, absence de sommaire sur les guides de quête, et une quinzaine de
  comptes comparés à un relevé de référence. Elle rend 1 en cas d'écart. Les
  deux défauts trouvés à l'œil aujourd'hui, tanières et raids sans étapes,
  auraient été dits par elle.
- Redémarrer le poste retient désormais la place des fenêtres. Elle n'était
  enregistrée qu'au « Quitter » : une fin de session Windows la perdait, et les
  fenêtres revenaient à leur place de l'avant-dernière fois. L'écriture est
  attendue avant l'arrêt, bornée à trois secondes des cinq que Windows accorde.
- L'application se met à jour depuis les livraisons du dépôt. Elle demande la
  dernière au démarrage, la télécharge en fond si la case « Se mettre à jour
  toute seule » est cochée, vérifie son empreinte, et pose le nouvel exécutable
  quand on quitte : jamais en pleine session. La note de version paraît au
  démarrage suivant, celui qui exécute enfin la nouvelle version, et un bandeau
  la rend consultable avant.
- Elle refuse de se mettre à jour depuis un arbre de sources : le lanceur de
  développement republie à chaque démarrage et écraserait la mise à jour dans la
  seconde, en faisant croire à une régression.
- Une chaîne de livraison publie l'exécutable, son empreinte et la note de
  version quand une étiquette « v… » est poussée. La marche à suivre est dans
  docs/LIVRAISON.md.
- Les guides tiennent l'application en vie à eux seuls. Fermer la dernière
  fenêtre de jeu, ou masquer les réglages, emportait le guide qu'on était en
  train de lire. Ils comptent désormais comme le panneau : tant qu'ils sont à
  l'écran, l'application continue ; les masquer alors qu'il ne reste rien
  d'autre l'arrête, comme masquer le panneau.
- Le panneau des prérequis s'ouvre à gauche du cadenas et non plus dessous, où il
  recouvrait les lignes suivantes et se calait sur un décalage fixe que la
  largeur du texte démentait. Un second clic sur le cadenas le referme.
- Ces quêtes s'enchaînent aussi entre elles au lieu de se ranger par titre. Les
  quatre-vingts quêtes d'alignement bontarien, qui se lisaient « bontarien 1,
  10, 11, 12, 2 », se lisent dans l'ordre.
- La chaîne de quêtes et la liste rangent enfin un succès de la même façon. La
  chaîne triait à l'endroit un rang de jeu inconnu, donc en tête, là où la liste
  le met en queue : une quête de rang inconnu passait pour la première de son
  succès et se donnait pour la suite de la série précédente.
- La fenêtre s'appelle « Guides » et non plus « Quêtes » : elle ouvre aussi des
  donjons, des raids, des tanières et des chemins. Le raccourci et l'infobulle
  du bouton disent de même.
- Le rond d'attente était dessiné à seize unités quel que soit sa taille : à
  vingt-deux pixels, l'arc bleu restait seize et se retrouvait décalé en haut à
  gauche d'un anneau plus grand que lui. Il est maintenant dessiné à taille fixe
  et mis à l'échelle. L'anneau et l'arc n'avaient d'ailleurs pas le même rayon,
  huit contre sept, ce qui les décalait d'un pixel entier même à seize.
- Le pied de succès disparaît quand il n'a rien à dire. Un donjon, un raid, une
  tanière et un chemin n'appartiennent à aucune suite : ils n'ont ni quête
  avant, ni quête après, ni rang dans un succès, et le pied ne montrait pour eux
  qu'un filet et une bande vide au-dessus de la source.
- Les tanières et les raids se parcourent enfin par leurs sections. Une page se
  lit par ses titres de second rang, mais les deux raids n'ont d'autre titre que
  « Sommaire » et sept tanières sur huit descendent les leurs au quatrième
  rang : on lit alors le sommaire que la page se donne. La tanière du Piou passe
  de une étape à sept, le Domaine du Dark Vlad de une à quatre. Rien ne change
  pour les donjons, les chemins et les quêtes, aucun des sept cent
  quatre-vingt-deux guides ne portant de sommaire.
- Trois des quarante liens de ces sommaires visent une ancre qui n'existe pas,
  « #salles » pour « salle ». Le texte du lien la retrouve, comparé aux
  identifiants de la page sans accents ni article.
- Le niveau d'un donjon se dit « niv. 100 » et non plus « 100 » : le nombre entre
  parenthèses se confondait avec ceux qui comptent les quêtes d'une zone.
- Le rond d'attente se pose en haut de la page et non plus en son milieu, où il
  tombait à cinq cents pixels sous le bandeau, dans une étendue vide qui se
  lisait comme une panne. La ligne d'étape, qui n'affichait alors que deux
  flèches éteintes, dit maintenant « Chargement de la page… ».
- Les chemins prennent une route en perspective ; l'épingle du lieu tenait la
  place, et un chemin n'est pas un lieu.
- Les quatre entrées de la racine ont chacune leur dessin : un parchemin, une
  tour crénelée, un crâne, une empreinte. La clef qui tenait la place des
  donjons a été rendue à son seul emploi, la colonne de droite, où elle dit
  qu'un donjon en exige une ; une recherche la montrait jusque-là deux fois sur
  la même ligne. Les trois lieux de combat gardent une teinte commune : la
  couleur dit le genre, la forme dit lequel des trois.
- Le champ de recherche dit ce qu'il accepte tant qu'on n'y a rien écrit : une
  zone, un succès, une quête ou un donjon. La croix qui le vide est passée
  dedans, où l'on voit ce qu'elle vide.
- Un donjon se suit comme une quête : un lien qui y mène reste dans la fenêtre,
  la liste rouvre sur sa branche, et il est retrouvé au lancement suivant.
- Une ancre de la page, comme « Aller directement à la mécanique du donjon »,
  ne passe plus pour une navigation étrangère et n'ouvre plus de seconde
  fenêtre.
- L'indicateur d'attente ne peut plus rester en l'air : il retombe si la vue
  échoue, et de toute façon au bout de vingt secondes, le journal disant alors
  qu'on a attendu pour rien. Le chemin de chargement est tracé de bout en
  bout.

- Les zones de quêtes suivent l'ordre de progression du jeu, sous leur nom court,
  avec un bloc « Quêtes supplémentaires » pour ce qui n'en relève pas.
- La recherche trouve aussi les zones et les succès, et les rend séparés ; les
  quêtes d'un succès trouvé sont listées sous lui.
- Un lien cliqué dans un guide ouvre une fenêtre à part, redimensionnable et
  toujours au-dessus, nettoyée du décor du site comme la fenêtre de quêtes.
- Le bandeau d'étape affiche un raccourci d'une phrase au lieu du paragraphe.
- Une carte devant les zones, une étoile devant les succès : la nature d'une
  ligne se voit sans la lire.
- Une croix vide la recherche, une autre ferme le panneau.
- Les fenêtres de l'application retrouvent leur place au lancement, y compris
  sur un second écran d'une autre densité.
- Le suivi de quêtes rouvre comme on l'a laissé, sur la dernière quête lue.
- Une icône annonce les prérequis d'une quête, et le survol les donne un par
  ligne, sous un titre et à la puce. Un clic épingle le panneau, et ceux qui
  sont des quêtes s'ouvrent d'un clic.
- Les quêtes se suivent au-delà de leur succès, par leurs prérequis : cent
  soixante-huit gagnent une suivante et cent quatre-vingt-dix-sept une
  précédente. Le bouton nomme la série d'arrivée quand on en change.
- Un indicateur d'attente prend la place du guide le temps qu'une page arrive.
- Une flèche revient sur la quête d'où l'on vient, quel que soit le chemin pris
  pour y arriver.
  Il manquait sur les liens de quête cliqués dans le guide : la navigation
  refusée puis relancée signalait sa fin après le départ de la vraie, et
  l'ancien guide restait à l'écran sans que rien ne l'annonce.
- Chaque nature de ligne porte son icône et sa couleur : un parchemin pour les
  quêtes, une clef pour les donjons, une épingle pour un lieu, un marque-page
  pour une famille de quêtes, une étoile pour un succès, un cadenas pour des
  prérequis. Les deux entrées de la racine sont dessinées pleines, les autres au
  filet.

### Modifié

- **Les touches de l'application se ressemblent enfin.** Ce qui se presse porte
  un angle de cinq pixels, ce qui porte du contenu en garde deux : la
  distinction se voit sans qu'on ait à la nommer, là où deux pixels sur une
  touche de trente de haut lisaient comme un rectangle inachevé. La face prend
  un dégradé à peine perceptible, plus clair en haut, et s'éclaircit sous le
  doigt au lieu de s'effacer : une baisse d'opacité rapprochait la touche du
  fond, ce qui est le contraire de ce qu'un survol doit dire.

- **La barre du bas est un groupe de touches nommées.** Trois icônes flottantes
  y surmontaient chacune son raccourci en chasse fixe : « Ctrl + » paraissait
  trois fois dans un pied de fenêtre qui ne demande qu'à se faire oublier, et il
  fallait survoler pour savoir laquelle faisait quoi. Un seul cadre, deux
  filets, et « Empiler », « Côte à côte », « Guides » écrits à côté de leur
  dessin. Les raccourcis se lisent dans l'onglet qui leur est consacré, et
  l'infobulle dit ce que chaque touche fait.

  Les trois dessins sont redevenus des tracés, comme partout ailleurs dans
  l'application : ils étaient bâtis en rectangles imbriqués, et celui du
  côte-à-côte se remplissait de son propre trait à cette taille.

- Le plus du bouton d'association perd sa pastille et redevient un tracé, du
  même trait que le signet de la touche voisine : la pastille lui donnait un
  poids que l'autre n'avait pas.

- Le rang de l'étape ne déplie plus rien quand le guide n'en a qu'une : la
  pastille y ouvrait une liste d'un seul élément.

- Le signalement porte le succès entre parenthèses : « Île d'Otomaï › Les
  chasses de Crocodaille Dandi (Service de dépannage) ».

- Le signalement porte la zone et la quête, et non plus le rang de l'étape. Ce
  rang est une numérotation qui n'existe que dans cette fenêtre : le site ne
  numérote pas ses paragraphes, et le repère ne désignait donc rien pour qui
  reçoit le signalement. Le champ dit maintenant « Astrub › La découverte d'un
  destin ».

- La fenêtre de signalement épouse le formulaire : sa hauteur s'ajuste à ce
  qu'il mesure, sa largeur ne s'étire plus et le bouton d'agrandissement
  disparaît. Elle s'appelle « Remonter une erreur sur Papycha ».

- Le bouton de signalement ne paraît plus que sur un guide. Le site ne met de
  formulaire qu'en pied d'article, et n'en a pas de général : ailleurs, le
  bouton menait à la page de contact, ce qui n'était pas ce qu'il promettait.

- L'icône des quêtes rejoint la teinte neutre des trois autres lignes de
  l'accueil des guides, où elle était la seule en couleur.

- La croix qui fermait la fenêtre d'un compte devient un carré, en paire avec le
  triangle de lecture. Dans une liste, une croix veut dire « supprimer cette
  ligne » pour à peu près tout le monde ; rien n'est supprimé ici, ni le compte,
  ni son profil Android, ni le jeu sur le téléphone. L'infobulle le dit
  maintenant, et ne renvoie plus à une case à cocher retirée depuis.

- Le bouton d'association et celui des profils forment une paire. Ils partagent
  la même ligne et n'avaient rien en commun : l'un cadré, l'autre flottant sans
  bord, avec un plus en simple caractère qui passait pour de la ponctuation et un
  chevron de fonte à chasse fixe qui pendait sous le texte. Même dessin
  désormais, une marque d'accent à gauche, et le bleu sourd sous le doigt à la
  place d'une baisse d'opacité qui ne disait rien.

- Les aides montrent le chemin de menu comme la suite d'écrans qu'il décrit :
  chaque écran porte son nom en titre et, sur l'une de ses lignes, le libellé
  exact de ce qu'on y touche. C'était une ligne de texte à chevrons. Rien n'a
  été rédigé pour cela, les fiches de marque écrivant déjà leurs chemins sous
  cette forme.

- Le premier lancement ouvre le panneau sur l'onglet Appareils, avec la fenêtre
  d'association par-dessus quand aucun téléphone n'est connu. La fenêtre de mise
  en route disparaît : elle refaisait ce que le panneau fait déjà, et la
  refermer arrêtait l'application. L'état du téléphone en toutes lettres, qui
  n'existait que là, est repris dans le panneau.

- Le retour est un bouton fixe sous le fil d'Ariane, avec sa flèche et son mot :
  il défilait avec la liste et disparaissait dès qu'on descendait.
- Le panneau de recherche prend toute la hauteur : le bandeau d'étape et le pied
  de succès s'effacent tant qu'il est ouvert.
- La barre de recherche prend toute la largeur ; un indicateur discret remplace
  le compte de quêtes pendant l'indexation.
- Une plage de niveaux n'est affichée que lorsqu'elle repose sur assez de
  quêtes : le site ne renseigne le niveau que sur 117 des 782.
- Les encarts du site et les apartés entre parenthèses ne comptent plus pour des
  étapes.
- Le départ d'une quête est une étape à part entière, la première. Il était
  plaqué sur le premier paragraphe du guide, qui n'a le plus souvent rien à
  voir : sur seize guides relevés, treize ouvrent sur un préambule.
- Le départ s'affiche enfin : le premier paragraphe passait la marque de lecture
  avant qu'on ait rien fait défiler, et l'étape 1 n'existait qu'en théorie.
- Une étape choisie au bouton n'est plus reprise par le défilement qu'on vient
  de demander, ni perdue sur un guide trop court pour défiler.
- Le résumé d'étape ne finit plus au milieu d'un mot : le nom capturé débordait
  sur la suite de la phrase seize fois sur dix-huit, coupé net au quarantième
  caractère. Il reconnaît en plus « reparlez », « en parlant », « vos adieux à »
  et « présentez-vous à », relevés sur le site.
- Le personnage de départ vient des métadonnées et n'était pas relu : deux
  quêtes sur six cent quatre-vingt-treize y logent une phrase, d'où « Parlez à
  bateau pour vous rendre au village d'Albuera ».
- Un paragraphe en gras ne suffit plus à faire une étape : il lui faut des
  coordonnées ou un ordre donné au lecteur. Mesuré sur cinquante-cinq guides,
  cinq cent trente-deux paragraphes en gras ne donnaient que trois cent
  cinquante-sept consignes ; le reste décrivait des sorts de boss, commentait un
  choix de dialogue ou titrait une liste, et se retrouvait résumé faute de mieux
  par sa première phrase.
- La suite d'une série se cherche dans tout le succès et non dans sa seule
  dernière quête : sept succès y gagnent une continuation, dont « Médiation
  expéditive », qui se prolonge depuis sa cinquième quête sur six.
- Rouvrir la liste montre la rubrique de la quête affichée, celle-ci
  surlignée. Elle rouvrait sur la racine ou sur une recherche.
- Une recherche sans résultat dit « Aucun résultat ».
- Le survol des prérequis dit « Prérequis » et non « À faire avant » : sur cinq
  cent soixante-sept prérequis distincts, on trouve des objets à apporter, un
  alignement, un nombre de joueurs, un niveau et des créneaux horaires.
- Dans une recherche, les titres « Zones », « Succès » et « Quêtes » dominent
  les succès qu'ils coiffent, au lieu de leur ressembler.
- « Ouvrir dans le navigateur » ouvre ce que la fenêtre montre : l'accueil du
  site à la racine du menu, la page des zones quand on les parcourt, celle d'une
  rubrique quand on y est entré, la quête sinon. Elle menait toujours à la quête, y compris quand la liste
  couvrait l'écran.
- Un blanc sépare « Quêtes principales » des lieux, qu'elle ouvrait sans en
  être un.
- Le crédit « Guides de papycha.fr » porte la couleur d'accent.
- La recherche ne retient plus une quête pour un mot qui n'existe que dans le
  nom de sa zone : « frigost » en rendait cent soixante-dix-sept, dont cent
  soixante-treize par ce seul chemin, et « bworks » onze sans qu'aucune ne porte
  le mot. C'est au groupe des zones que revient cette recherche-là.
- Les prérequis remplacent le niveau à droite d'une quête : le site les donne
  pour six cent treize quêtes contre cent dix-sept pour le niveau.
- Un lien vers une quête du catalogue est suivi dans la fenêtre plutôt que dans
  une seconde, comme le bouton « précédente » qui mène au même endroit.
- Le bloc « Quêtes précédentes » du site disparaît des guides : le pied de la
  fenêtre y mène déjà. Le bandeau d'intro entier part avec lui quand il ne lui
  reste rien à dire, ce qui est le cas hors des quêtes répétables.

### Corrigé

- L'échelle de qualité allait à l'envers. Le débit était fixé par palier alors
  que la définition et la cadence, elles, changeaient : mesuré en bits par pixel
  et par image, ce qu'un encodeur reçoit vraiment, « maximale » en accordait
  cinq fois et demie moins que « basse » et rendait donc une image plus
  grossière en mouvement. Le débit suit maintenant la définition et la cadence
  réellement retenues. Sur le même écran et la même fenêtre, « maximale » passe
  de 0,029 à 0,089 bit par pixel, soit trois fois plus de bits par image.
- La cadence maximale descend de 120 à 60 images par seconde. Mesuré sur le jeu,
  il en rend trente-huit : les cent vingt ne servaient qu'à diviser par deux les
  bits accordés à chaque image qui existe vraiment.
- Le journal dit désormais, à chaque ouverture, la définition, la densité, la
  cadence et le débit retenus. Ils dépendent de la fenêtre, de l'écran et du
  palier, et ne se lisaient nulle part.
- Les succès retrouvent le rang que le site leur donne. Le site n'écrit pas le
  même nom aux deux endroits où il nomme un succès : l'intertitre dit « Brûler
  le pissenlit à la racine », « Fri Carré », « Etre plus royaliste que le roi »,
  la quête dit « par la racine », « Fri carré », « Étre » ; ailleurs c'est une
  coquille franche, « Globlitération » contre « Goblitération ». Le rang se
  prenait sur l'intitulé, si bien que trente des quatre-vingt-seize entrées de
  la liste des rangs ne désignaient aucun succès, et que quarante-neuf succès
  sur cent quinze n'en avaient pas. Il se prend maintenant sur les quêtes que
  l'intertitre coiffe : quatre-vingt-dix-sept rangs, aucun nom en trop, dix-huit
  succès sans rang. Rangés par ordre alphabétique en fin de zone, faute de tout
  signal : douze, contre trente-huit.
- Une page pouvait faire tomber l'application en trois lignes. Le pont est posé
  sur tout document que la fenêtre des guides charge, et la fenêtre lisait ce
  qu'il lui postait sans précaution : un message qui n'a pas le champ « kind »,
  ou qui n'est pas du texte, levait dans un gestionnaire d'événement, où
  personne ne rattrape. Mesuré sur neuf formes qu'une page peut poster, neuf
  levaient. La lecture est descendue dans le noyau, où elle se vérifie, et rend
  désormais un message ou rien.
- Nos fenêtres ne chargent plus que le site. Elles n'ont pas de barre
  d'adresse et portent notre cadre : tout lien qui sort du site part maintenant
  au navigateur, où l'on voit où l'on va. La fenêtre des pages liées prenait
  jusqu'ici n'importe quelle adresse, « file:// » compris, et ne retenait ni la
  navigation ni les ouvertures en fenêtre neuve.
- Suivre la quête précédente ou suivante éteignait la navigation : le pied se
  vidait après un seul saut et il fallait repasser par la liste.
- Rouvrir le panneau sélectionne la quête ouverte, sans reconstruire la liste.
- La reconnaissance des fenêtres de l'application interrogeait la fenêtre de
  quêtes depuis le guet du premier plan, qui ne vit pas sur le fil de
  l'interface : chaque changement de fenêtre levait une exception et les
  raccourcis restaient dans leur état précédent.
- Les sept rubriques venues d'une page du site n'étaient cherchables par aucun
  chemin, et taper « quête » ramenait le catalogue entier.
- Le bouton de retour en haut du site et sa bande verte réapparaissaient au
  premier défilement : ils étaient masqués par style en ligne, que le fondu du
  greffon écrasait.
- Le haut d'une page dépassait dès que la fenêtre passait cinq cent quarante
  pixels de large, seuil sous lequel le site annule lui-même une marge de
  cinquante pixels.
- Le relevé des succès retirait des intitulés de prérequis un préfixe que le
  site n'emploie plus : douze liens de l'ordre de jeu se perdaient en silence.
- La lecture du fichier de succès livré était sensible à la casse et n'y voyait
  aucune entrée, sans le dire.

- Suivi de quêtes adossé à papycha.fr : fenêtre toujours au-dessus ouverte par
  `Ctrl+Q`, recherche par rubrique et par succès sur un catalogue de 782 quêtes,
  sélecteur d'étape et navigation dans la chaîne du succès.
- Contrôle de la version d'Android avant le lancement : un appareil sous
  Android 10 ou plus ancien est écarté aussitôt, en disant sa version, au lieu
  d'échouer au bout de trente secondes sur un message deviné.
- Fiches d'aide pour vivo et iQOO, et pour Amazon Fire, avec l'avertissement que
  Fire OS n'a pas le Play Store.
- Manifeste applicatif déclarant la conscience de la mise à l'échelle écran par
  écran et le socle Windows 10 1809.

### Modifié

- Le repli de définition descend les paliers au lieu de s'arrêter à 1080, ce qui
  couvre enfin les encodeurs plafonnés à 1280x720, et conserve le rapport
  d'image de l'écran au lieu d'imposer du 16:9.
- Les refus de scrcpy sont rangés en catégories : seuls ceux qu'une définition
  plus modeste peut réparer sont retentés. Un téléphone débranché ne coûte plus
  une seconde attente.
- La détection du jeu retient les copies installées sous un nom de paquet
  dérivé, que la comparaison stricte rendait invisibles.
- Le repli de la liste des profils Android est signalé dans le bandeau au lieu
  de passer pour un appareil qui n'a qu'un profil.
- Les chemins de menu de l'aide valent aussi pour une tablette, et les trois
  fenêtres d'aide disent qu'ils supposent un appareil réglé en français.
- Le repli d'Android sans surcouche nomme les constructeurs qu'il couvre :
  ASUS, TCL, ZTE, HMD, Fairphone, Transsion.

### Corrigé

- La densité d'affichage était bornée à 800 au calcul puis rabotée à 640 juste
  avant scrcpy : tout ce qui se trouvait entre les deux disparaissait en
  silence, et le zoom le plus proche saturait avant la hauteur annoncée.
- Un codec vidéo inconnu de scrcpy est écarté au lieu de lui être transmis.
- Depuis la recherche de quêtes, la flèche du bas s'arrêtait sur un intertitre
  de rubrique et Entrée ne faisait rien.

- Squelette de la solution .NET 10 : `DtHub.Core`, `DtHub.Infrastructure`,
  `DtHub.App` (WPF) et `DtHub.Tests` (xUnit).
- Identité produit centralisée pour permettre un renommage simple.
- Icône applicative générée par script reproductible.

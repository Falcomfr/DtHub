# Wait for the phone where the game was

When a phone's link drops, its game windows used to vanish, and the panel
popped up to say "liaison perdue". The window or tab came back later, at its
remembered place. The player saw a grey window, then nothing, then a window
appearing. Asked on 2026-10-26: show a "waiting for Wi-Fi" state where the
game was, and bring the game back into it.

## Behaviour

An account is *waiting* while its window is lost to its phone: recovery is
about to reopen it, or it waits for the phone to come back. It is also
waiting while its window is still open but the phone has not answered pings
for 3 s (the link is probably gone but not yet declared lost at 15 s).

A waiting account shows, in place of the game:

- a crossed Wi-Fi icon;
- "En attente du téléphone" and one line: "La liaison Wi-Fi est coupée. Le
  jeu revient ici dès qu'elle reprend.";
- a "Fermer" button, which closes the account as the window's cross does
  (D207: it leaves the next startup).

In the tabbed frame, the tab stays in the bar and its hosting area shows the
waiting view. In a free window, a small DT Hub window stands at the exact
rectangle of the game window. When the game is back, it takes the same tab,
or the same rectangle, and the waiting view goes away. If the link answers
again before 15 s, the frozen window is shown again as it was.

The game itself restarts on the phone when its window is reopened (the
virtual display dies with the link, and Android ends the game with it). This
design does not change that.

## Reconnection without the panel

The panel used to pop up because its sweep is what reconnects phones. While
accounts wait, the window watch, which runs whether the panel shows or not,
now drives the same reconnection pass. The panel no longer pops up for a
lost link; its banner keeps the "liaison perdue" line for whoever opens it.
Other reasons a window went away (WindowGone) still reveal the panel.

## Implementation notes

- `GameTabViewModel.Window` becomes settable, with `IsWaiting`. `KeepOnly`
  keeps waiting accounts' tabs (window cleared, `IsWaiting` set) instead of
  detaching them, so the frame no longer closes itself empty during a loss.
  `Attach` fills a waiting tab rather than refusing its key. `Select`,
  `Place` and visibility calls skip a cleared window.
- Free windows: the rectangle is the live window's at 3 s, or the last one
  `WindowManagerService` saw for the session. It is saved as the account's
  window rectangle, so the reopening lands on it.
- The 15 s guard (`PingSpikes.LinkLost`) is unchanged. The 3 s state reads
  the same pings.

## Not covered

A game that crashes on the phone, and scrcpy crashing on the PC, are other
findings of the same campaign, handled separately.

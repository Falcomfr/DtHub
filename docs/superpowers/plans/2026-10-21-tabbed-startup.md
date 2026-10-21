# Reveal tabbed games at their final place

> Execute inline with superpowers:executing-plans and review with
> superpowers:requesting-code-review.

**Goal:** Remove the visible large-to-small transition when a game opens in tabs.

**Architecture:** Park the SDL startup window offscreen without changing its
resolution or requested dimensions. Hide, position and dock it in PrepareWindow
before launching the game. Reveal the first native child only after its WPF
host has restored its position, measured the real tab and settled its layout.

**Tech stack:** .NET 10, WPF, Win32, scrcpy 5.0.1, xUnit.

- [x] Add command-line and placement regressions before implementation.
- [x] Add launch-only StartOffscreen and keep its logical placement separate.
- [x] Dock from PrepareWindow; keep desktop restoration away from docked children.
- [x] Defer the first native reveal; preserve selected tabs in existing frames.
- [x] Test cancellation before display readiness and while preparing the window;
  observe both surviving-process failures, then stop cancelled offscreen sessions.
- [x] Build the solution and run all 2,505 tests successfully.
- [x] Review the lifecycle and cancellation changes; resolve the cancellation hole.
- [x] Verify real SDL offscreen creation, native docking/reveal and safe detach
  with the production WPF tab frame and Win32 controller, without launching a game.

The development probe is outside the repository at `C:\Dev\DTHub-startup-probe`.
It creates only an empty Android virtual display and closes its own SDL process.
On the primary screen: initial SDL frame 662x416 at (-32011,-32045), final host
1200x772 with child 1178x663, visible and docked. Detach restored (100,100).
The player's actual game startup still requires a retest.

The repeat using the application's PerMonitorV2 manifest on the second screen
passed: host 1200x740 at (3940,571), child 1184x666 at (3948,637), visible and
docked. Detach returned to (100,100). The native capture is kept as ignored
output at `build/app-startup-probe-secondary.png`.

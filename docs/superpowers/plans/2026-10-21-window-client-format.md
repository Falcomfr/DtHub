# Fit the game to its client area

> Execute inline with superpowers:executing-plans; review with
> superpowers:requesting-code-review before completion.

**Goal:** Fill an ordinary game window from its actual client dimensions.

**Architecture:** Select the outer launch rectangle, measure its frame on its
monitor, then derive the display aspect from its client rectangle. Keep the
chosen launch rectangle for restoration. Keep a fixed display while playing.

**Tech stack:** .NET 10, WPF, scrcpy, xUnit.

## Constraints

Keep phone-format and portrait-tool behavior. No runtime display-height changes,
no game restart during resize, no crop or stretch. Use dotnet.exe from Windows
paths. Keep Core pure; new test names and diagnostic decisions are French.

## Task 1: Launch geometry and display aspect

- [x] Add regression tests in WindowManagerServiceTests for the full-height
  legacy rectangle, preserved small/custom rectangles and destination frame.
- [x] Run the regression and observe the incorrect retained 1813px width.
- [x] Update PreviewGameArea for unconstrained launches and WindowChrome for
  the target monitor. Use the actual client ratio in DisplayLadder.For via an
  optional windowAspect argument, keeping phoneAspect first in precedence.
- [x] Wire GameLauncher.ComputePlacement and WithDisplayFor; restore from the
  selected launch rectangle rather than an older saved rectangle.
- [x] Add and run ratio, quality-ceiling, phone-precedence and fullscreen tests.
- [x] Run dotnet.exe build DtHub.slnx and dotnet.exe test DtHub.slnx.
- [x] Record the behavior and hardware limitation in DECISIONS and status;
  review the final diff and resolve review findings.

## Verification outcome

The original 1813px regression failed before implementation. The complete suite
passes (2,498 tests); the solution builds with zero warnings and zero errors.
The review's portrait/phone restoration finding was fixed and reviewed again.
The Release single-file executable is at
`build/review-window-client/publish/DtHub.exe` (ignored build output).

Game rendering on the remote POCO remains unverified. A production DT Hub
instance is already running locally; launching the test executable would only
signal that instance through its singleton mutex, not exercise the new code.
No shell capture is presented as evidence of corrected Android rendering.

# Fit the game to its launch window

The ordinary game display must use the aspect ratio of the window's client
area at launch. The title bar, borders and taskbar are not part of that area.
The default outer window occupies the requested share of the work area.
Small remembered windows keep their rectangle. An old window matching the
previous full-height, monitor-ratio fit expands to the work area; other
remembered rectangles are preserved. Phone-format overrides and portrait tools
retain their explicit format.

Measure the frame on the destination monitor. A fullscreen launch uses the
screen bounds and no frame for the display ratio. Pass the same selected outer
rectangle to restoration so that it cannot undo the launch calculation.

Keep the virtual resolution fixed during play. D16/D17 demonstrate that changing
its height breaks the game's layout. Subsequent resizing keeps the source
aspect. Switching an existing window to fullscreen can therefore retain bands
when the two formats differ; a fullscreen launch uses the monitor format.
This change must not restart a running game, crop it, stretch it or reinstate
flex-display.

Validate the reported 1813x1050 rectangle on a 1920x1080 monitor, custom saved
sizes, destination DPI, phone-format precedence, resolution/quality ceilings,
and fullscreen startup. Physical phone rendering requires a player retest.

## Follow-up: reveal tabbed windows directly in their frame

The player observed a large standalone window before the remembered smaller
tab frame. Keep the fixed virtual display, but start tab-bound SDL windows
offscreen. Before launching the game, hide the native window, move it to its
real standalone position (the one docking saves for a future detach), then dock
it. Restore the chassis, lay out an actual tab header and settle the child
before revealing the first frame and child. Existing frames retain their
selected tab. Final desktop restoration must skip actual docked children.

A docking failure reveals the correctly positioned standalone window. A
cancelled offscreen startup stops its session; an empty failed host closes.
No gameplay automation or runtime display-height changes are involved.

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

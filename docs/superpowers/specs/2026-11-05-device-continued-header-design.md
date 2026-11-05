# Show a split phone as "continued", not as a second phone

A player reported on 2026-11-05 that "the devices are shown twice". Nothing was
detected twice: the account order is global and free (list, tabs and F1-F8 all
follow it), and moving one tab had put the Redmi's account between two POCO
accounts. The list repeats a phone's full header at each run of its accounts,
so a split phone looks like a second phone.

## Approaches

- **A. Keep the free order, mark the repeat as a continuation (picked).** The
  free order is deliberate: it is what lets F1 sit on one phone and F2 on
  another. Only the reading was wrong.
- B. Always group by phone. Removes the split, but also the cross-phone F-key
  and tab order players can choose today.
- C. Separate tab order from list order. Then F1-F8 would disagree with one of
  the two, which is worse.

## Design

- The first run of a phone keeps its full header and its problems line,
  unchanged.
- A later run of the same phone gets one slim muted line instead:
  `↳ <phone name> · continued`, with no dot, battery, link chips, screen
  switch, buttons or problems line. Its tooltip: "Same phone as above: the
  list order also sets the startup, tab and F1-F8 order. Drag its accounts
  together to regroup them."
- Same on Windows and Mac; strings in en, fr, es.
- The order, the drag rules and the tab reordering do not change.

## Left to the builder

Exact spacing of the slim line, matched to the existing muted styles.

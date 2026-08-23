import type { MouseEvent } from "react";

// A modified click on a link does not navigate THIS document: the browser opens
// the destination in a new tab, a new window, or saves it, and the user stays
// exactly where they were. Any dismiss wired to `onClick` therefore fires
// against a surface the user never left -- the popover closes, the menu shuts,
// focus jumps -- while the page under it is unchanged.
//
// Enter still dismisses: a click synthesised from keyboard activation carries
// `button === 0` and no modifier, so it passes. (Space scrolls rather than
// activating an `<a href>`, and every consumer here is a link.)
export function onPlainNav(event: MouseEvent<Element>, fn: () => void): void {
  if (
    event.metaKey ||
    event.ctrlKey ||
    event.shiftKey ||
    event.altKey ||
    event.button !== 0
  ) {
    return;
  }
  fn();
}

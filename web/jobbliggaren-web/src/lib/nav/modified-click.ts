import type { MouseEvent } from "react";

// A modified click on a link does not navigate THIS document: the browser opens
// the destination in a new tab, a new window, or saves it, and the user stays
// exactly where they were. Any dismiss wired to `onClick` therefore fires
// against a surface the user never left -- the popover closes, the menu shuts,
// focus jumps -- while the page under it is unchanged.
//
// The branch is written once, here. A bare `isModifiedClick` predicate would
// leave every call site to write its own `if (...) return;`, which is one
// chance per site to get the polarity backwards -- and a backwards polarity is
// silent, because the surface then dismisses exactly as it did before the fix.
//
// The signature takes the event rather than returning a handler, so it runs at
// event time. A combinator shape (`onClick={onlyOnPlainNav(fn)}`) is evaluated
// during render, and `react-hooks/refs` rejects that wherever the wrapped
// callback reads a ref -- which the drawer's handler does, to restore focus.
// Measured: the combinator shape produced 3 lint errors against a 0-error
// baseline. Call sites still carry no conditional of their own.
//
// Keyboard activation needs no special case: a click synthesised from Enter or
// Space carries `button === 0` and no modifier, so it passes and still dismisses.
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

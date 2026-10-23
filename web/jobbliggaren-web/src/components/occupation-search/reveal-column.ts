/**
 * A `useFocusAfterCommit` target: scrolls the opened column into view only as far as it needs, and
 * focuses its heading.
 */
export function revealColumn(
  column: HTMLElement | null,
  heading: HTMLElement | null,
): HTMLElement | null {
  column?.scrollIntoView?.({ block: "nearest" });
  return heading;
}

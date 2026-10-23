/** A `useFocusAfterCommit` target: scrolls the opened column to the top and focuses its heading. */
export function revealColumn(
  column: HTMLElement | null,
  heading: HTMLElement | null,
): HTMLElement | null {
  column?.scrollIntoView?.({ block: "start" });
  return heading;
}

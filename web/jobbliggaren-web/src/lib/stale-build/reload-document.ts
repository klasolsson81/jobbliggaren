/**
 * reloadDocument — replaces the document for stale-build recovery (ADR 0148)
 * or an explicit retry of a failed chunk load. Its own module so the hook,
 * the core and the boundary tests can mock the seam (`vi.mock`) instead of
 * `window.location`, which jsdom does not let a test replace.
 */
export function reloadDocument(): void {
  window.location.reload();
}

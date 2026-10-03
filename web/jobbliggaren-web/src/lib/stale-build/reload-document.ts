/**
 * reloadDocument — the one call that replaces the document when a page from a
 * previous build meets the current one (ADR 0148). Its own module so the hook,
 * the core and the boundary tests can mock the seam (`vi.mock`) instead of
 * `window.location`, which jsdom does not let a test replace.
 */
export function reloadDocument(): void {
  window.location.reload();
}

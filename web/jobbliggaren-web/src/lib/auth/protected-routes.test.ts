import { readdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { PROTECTED_PREFIXES } from "./protected-routes";

/**
 * Freezes the ADR 0017 defense-in-depth invariant: `PROTECTED_PREFIXES` mirrors
 * the `(app)` route group exactly. The expected set is DERIVED from the filesystem,
 * so a new `(app)` route that is not listed — or a stale prefix whose route was
 * deleted — fails CI instead of drifting silently.
 *
 * Regression guard for #513, where the list had drifted: stale `/mig` (route gone)
 * plus missing `/aktivitetsrapport` `/matchningar` `/ny-ansokan` `/statistik`.
 */
const APP_ROUTE_GROUP = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../../app/(app)"
);

/**
 * Every top-level `(app)` directory is an authenticated URL prefix, EXCEPT
 * segments that do not contribute a URL path:
 *   - parallel-route slots  (`@modal`)      → start with `@`
 *   - private folders        (`_internal`)   → start with `_`
 *   - route groups           (`(group)`)     → start with `(`
 *   - dynamic segments       (`[id]`)        → start with `[` (not a static prefix)
 * Files (`layout.tsx`, `default.tsx`, …) are excluded by the `isDirectory` filter.
 */
function derivePrefixesFromRouteGroup(): string[] {
  const dirs = readdirSync(APP_ROUTE_GROUP, { withFileTypes: true }).filter(
    (entry) =>
      entry.isDirectory() &&
      // Not URL-contributing: parallel-route slots (@modal), private folders
      // (_internal), and dotfiles.
      !entry.name.startsWith("@") &&
      !entry.name.startsWith("_") &&
      !entry.name.startsWith(".")
  );

  // Fail loud on structures this flat derivation does not model: a nested route
  // group `(x)/` puts its children on sibling URLs, and a dynamic segment `[id]`
  // is not a static prefix — either could let PROTECTED_PREFIXES pass this test
  // while the middleware silently misses a gate. (app) is flat today; if it ever
  // grows one, teach this test (and the list) rather than let the guard drift.
  const unsupported = dirs.filter(
    (entry) => entry.name.startsWith("(") || entry.name.startsWith("[")
  );
  if (unsupported.length > 0) {
    throw new Error(
      "(app) has nested route-group/dynamic segments this invariant does not model: " +
        unsupported.map((entry) => entry.name).join(", ")
    );
  }

  return dirs.map((entry) => `/${entry.name}`).sort();
}

describe("PROTECTED_PREFIXES", () => {
  it("mirrors the (app) route group exactly (ADR 0017 defense-in-depth)", () => {
    const expected = derivePrefixesFromRouteGroup();
    const actual = [...PROTECTED_PREFIXES].sort();

    expect(actual).toEqual(expected);
  });
});

import { readdirSync, statSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * Fitness function for the `@modal` slot's route coverage (#1488).
 *
 * The defect it closes: `@modal/[...catchAll]/page.tsx` sat at the SLOT ROOT, so the
 * `(app)` group matched every otherwise-unmatched path. `(app)/layout.tsx` then ran,
 * `getServerSession()` found no session, and a logged-out visitor who mistyped a URL
 * was redirected to `/logga-in` instead of getting the site's 404. Measured against
 * `pnpm dev` at `d4cf0552`: `/cvmall` answered `307 -> /logga-in`; with the catch-all
 * removed the same URL answered `404` and rendered the root `not-found.tsx`.
 *
 * A page-level fix is impossible: the layout runs BEFORE the page, so its redirect
 * preempts any `notFound()` the slot could call. The group must stop matching.
 *
 * The catch-all was not decoration — Next's own docs (16.3,
 * `03-file-conventions/parallel-routes.md`) make it the mechanism that closes a
 * modal on client-side navigation to a route the slot no longer matches; `default.tsx`
 * only covers hard-nav. The same docs give the narrower form this replaces it
 * with: a slot page per destination. Measured with an isolated probe at `d4cf0552` —
 * a plain slot `page.tsx` closes the modal on soft-nav exactly as the catch-all did,
 * and an intercepting route still wins over a competing normal route in the same slot.
 *
 * So the slot must cover the app's REAL route space and nothing beyond it. That set is
 * DERIVED from the filesystem here, never listed — a list is the silent hole
 * `route-boundaries.test.ts` names, and `protected-routes.test.ts` already proves the
 * derivation idiom on this very directory.
 *
 * `(guest)/gast/@modal/[...catchAll]` is deliberately NOT covered: it is scoped under a
 * real URL segment, and its layout does not gate, so `/gast/zzz` already answers 404.
 */
const APP = resolve(dirname(fileURLToPath(import.meta.url)));
const MODAL = resolve(APP, "@modal");

const isDir = (path: string): boolean => {
  try {
    return statSync(path).isDirectory();
  } catch {
    return false;
  }
};

const isFile = (path: string): boolean => {
  try {
    return statSync(path).isFile();
  } catch {
    return false;
  }
};

/**
 * Directory names that contribute nothing to a URL, and are therefore never a route
 * segment: parallel-route slots, private folders, dotfiles. Same filter as
 * `protected-routes.test.ts` — one rule, two consumers.
 */
const contributesUrlSegment = (name: string): boolean =>
  !name.startsWith("@") && !name.startsWith("_") && !name.startsWith(".");

/** Top-level `(app)` directories that are real URL prefixes. */
function appSegments(): string[] {
  const dirs = readdirSync(APP, { withFileTypes: true }).filter(
    (entry) => entry.isDirectory() && contributesUrlSegment(entry.name)
  );

  // Fail loud on shapes this flat derivation does not model, exactly as
  // protected-routes.test.ts does: a nested route group puts its children on sibling
  // URLs and a dynamic segment is not a static prefix. Either would let the coverage
  // rule below pass while the slot silently missed a path.
  const unsupported = dirs.filter(
    (entry) => entry.name.startsWith("(") || entry.name.startsWith("[")
  );
  if (unsupported.length > 0) {
    throw new Error(
      "(app) has nested route-group/dynamic top segments this invariant does not model: " +
        unsupported.map((entry) => entry.name).join(", ")
    );
  }

  return dirs.map((entry) => entry.name).sort();
}

/** Direct children of `@modal` that are plain route segments — not interceptors. */
function modalSegmentDirs(): string[] {
  return readdirSync(MODAL, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && !/^\(\.{1,3}\)/.test(entry.name))
    .map((entry) => entry.name)
    .sort();
}

/**
 * Does this `(app)` segment own any URL deeper than `/<segment>`?
 *
 * Route groups are dropped before measuring depth, which is the whole reason this is
 * computed rather than eyeballed: `cv/(hub)/page.tsx` sits two directories down but its
 * URL is `/cv` itself, so it is NOT a nested route.
 */
function hasNestedRoute(segment: string): boolean {
  const walk = (dir: string, depth: number): boolean => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const child = resolve(dir, entry.name);
      if (entry.isDirectory()) {
        if (!contributesUrlSegment(entry.name)) continue;
        const isRouteGroup = entry.name.startsWith("(");
        if (walk(child, isRouteGroup ? depth : depth + 1)) return true;
        continue;
      }
      if (entry.name === "page.tsx" && depth > 0) return true;
    }
    return false;
  };

  return walk(resolve(APP, segment), 0);
}

describe("@modal slot coverage (#1488)", () => {
  it("covers exactly the (app) route space — no slot-root catch-all", () => {
    // Equality, not containment: a re-added `[...catchAll]` at the slot root is a
    // directory that is not an (app) segment, and fails HERE. That is the rule that
    // closes the defect class; the coverage rules below only keep the modal closing.
    expect(modalSegmentDirs()).toEqual(appSegments());
  });

  it("gives every (app) segment a slot page that closes the modal", () => {
    const segments = appSegments();

    // Anti-vacuity floor. Every rule in this file iterates a derived set, so a walk
    // that silently returned nothing would pass all of them. Asserting the app still
    // has its sections makes "derived nothing" a failure rather than a green tick.
    expect(segments.length).toBeGreaterThanOrEqual(12);

    const missing = segments.filter(
      (segment) => !isFile(resolve(MODAL, segment, "page.tsx"))
    );
    expect(missing).toEqual([]);
  });

  it("gives every (app) segment with nested routes a catch-all under that segment", () => {
    const nested = appSegments().filter(hasNestedRoute);

    expect(nested.length).toBeGreaterThanOrEqual(1);

    const missing = nested.filter(
      (segment) => !isDir(resolve(MODAL, segment, "[...rest]"))
        || !isFile(resolve(MODAL, segment, "[...rest]", "page.tsx"))
    );
    expect(missing).toEqual([]);
  });
});

import { readdirSync, readFileSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { V3_NATIVE_ROUTES, isV3Native } from "./v3-native-routes";

/**
 * Freezes the obligation `V3_NATIVE_ROUTES` creates (#1062).
 *
 * `AppShell` renders a v3-native route's children DIRECTLY in `.jp-content`, which sets
 * only `flex: 1` and `width: 100%`. So a page under one of these prefixes that owns no
 * width container renders flush to the viewport edge, with no max-width and no page
 * padding — and **nothing could detect it**, because the list lived inside a client
 * component and the pages were only ever read one at a time.
 *
 * Two had drifted by the time anyone measured. `/cv/granska/[parsedId]`'s review panel
 * was **3440px wide at a 3440px viewport** with its `h1` at x=0; `/cv/importera` was
 * identical. Both were reachable from `/cv` in one click, and `/cv/granska/[parsedId]`
 * is where the hub's "Kräver åtgärd" card sends every user with an unsaved import.
 *
 * The prefix match is what makes this a standing trap rather than a one-off: `/cv` opts
 * in every `/cv/**` descendant at once, so a route added under an existing prefix
 * inherits the obligation silently and no reviewer is prompted to check it.
 *
 * ⚠ **What this test can and cannot see.** It reads source text; it does not render.
 * It therefore proves that a container class is WRITTEN, never that the rendered box is
 * correct — that is the live/E2E measurement's job. It is a regression guard against
 * silent omission, which is the failure that actually happened, and it is deliberately
 * fail-closed: a file it cannot classify fails rather than passes.
 */
const APP = resolve(dirname(fileURLToPath(import.meta.url)), "../../app/(app)");
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), "../..");

/** A page owns its width when it renders either shell. Both are defined in globals.css. */
const CONTAINER_CLASSES = ["jp-pagehero", "jp-container"];

type RouteFile = { file: string; url: string; source: string };

/**
 * Every `page.tsx`/`loading.tsx` under `(app)`, with the URL it serves.
 *
 * `loading.tsx` is included deliberately and is not padding: a Suspense fallback paints
 * in the same slot, under the same shell rules, and `/cv/granska/[parsedId]`'s carried
 * the identical defect. Guarding `page.tsx` alone would fix the enumeration and miss the
 * property.
 */
function collectRouteFiles(dir: string, segments: string[] = []): RouteFile[] {
  const out: RouteFile[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      // Parallel-route slots (@modal) mount beside children and never own page width;
      // route groups ((x)) contribute no URL segment; private folders (_x) are not routes.
      if (entry.name.startsWith("@") || entry.name.startsWith("_")) continue;
      const next = entry.name.startsWith("(") ? segments : [...segments, entry.name];
      out.push(...collectRouteFiles(full, next));
      continue;
    }
    if (entry.name !== "page.tsx" && entry.name !== "loading.tsx") continue;
    out.push({
      file: full,
      url: "/" + segments.join("/"),
      source: readFileSync(full, "utf-8"),
    });
  }
  return out;
}

/** Resolve a `@/…` import to a file on disk, or null when it is not a local module. */
function resolveLocalImport(spec: string): string | null {
  if (!spec.startsWith("@/")) return null;
  const base = join(SRC, spec.slice(2));
  for (const candidate of [`${base}.tsx`, `${base}.ts`, join(base, "index.tsx")]) {
    if (existsSync(candidate)) return candidate;
  }
  return null;
}

const ownsContainer = (source: string) =>
  CONTAINER_CLASSES.some((c) => source.includes(c));

const routeFiles = collectRouteFiles(APP).filter((f) => isV3Native(f.url));

describe("V3_NATIVE_ROUTES — every page under a v3-native prefix owns its width", () => {
  it("finds route files to check (the guard is not vacuously green)", () => {
    // Without this, a broken derivation — a wrong path, a filter that matches nothing —
    // would make every assertion below iterate an empty list and report success. The
    // count is a floor, not a pin: new routes must not have to edit this number.
    expect(routeFiles.length).toBeGreaterThanOrEqual(20);
    expect(V3_NATIVE_ROUTES.length).toBeGreaterThan(0);
  });

  it.each(routeFiles.map((f) => [f.url, f] as const))(
    "%s owns a container, delegates to one, or renders nothing",
    (_url, routeFile) => {
      const { file, source } = routeFile;
      const rendersOwnMarkup = source.includes("className=");

      if (rendersOwnMarkup) {
        expect(
          ownsContainer(source),
          `${file} renders its own markup under a v3-native prefix but contains neither ` +
            `"jp-pagehero" nor "jp-container". AppShell gives these routes no width ` +
            `container, so this page renders edge-to-edge at every viewport. Wrap it the ` +
            `way the (app) standard does: a .jp-pagehero band, then .jp-container.jp-page.`,
        ).toBe(true);
        return;
      }

      // Renders no markup of its own: either it delegates to a component that owns the
      // shell, or it is a gate that renders nothing at all. Follow one hop before
      // concluding anything — an allowlist here would hide the guard's own under-reach.
      //
      // Only `@/components/…` counts as delegation. Narrowed after the first run: the
      // pattern matched every `@/…` import, so the six deferred-feature 404 stubs were
      // classified as delegating purely because they import `@/lib/auth/session` for
      // their session gate, and then failed for not rendering a shell they must not
      // render. Delegation order still comes BEFORE the gate check on purpose — a page
      // that both redirects on auth AND renders a component (e.g. /oversikt) must be
      // judged on the component, or the gate branch would let it through unchecked.
      const imports = [...source.matchAll(/from\s+"(@\/components\/[^"]+)"/g)].map(
        (m) => m[1]!,
      );
      const delegates = imports
        .map(resolveLocalImport)
        .filter((p): p is string => p !== null)
        .map((p) => readFileSync(p, "utf-8"));

      if (delegates.length > 0) {
        expect(
          delegates.some(ownsContainer),
          `${file} renders no markup of its own and delegates to ` +
            `${delegates.length} local component(s), none of which contains a container ` +
            `class. One hop is as far as this guard follows: if the shell genuinely lives ` +
            `deeper, move it up to the page or to the component this page renders.`,
        ).toBe(true);
        return;
      }

      // Neither markup nor a local component. The only legitimate remaining shape is a
      // route that navigates away instead of rendering — a deferred-feature 404 stub, a
      // redirect. Anything else is a shape this guard does not model, and it fails rather
      // than passing quietly.
      expect(
        /notFound\(\)|redirect\(|permanentRedirect\(/.test(source),
        `${file} renders no markup, imports no local component, and never navigates ` +
          `away. This guard cannot classify it. Teach the guard rather than leave the ` +
          `page unchecked.`,
      ).toBe(true);
    },
  );
});

describe("V3_NATIVE_ROUTES — the list itself", () => {
  it("has one home, and app-shell reads it from there", () => {
    // The list was inlined in app-shell.tsx until #1062. A second copy would let the
    // shell and this guard disagree about which routes carry the obligation — the guard
    // would then pass while the shell opted a page out.
    const shell = readFileSync(
      resolve(SRC, "components/shell/app-shell.tsx"),
      "utf-8",
    );
    expect(shell).toContain('from "@/lib/layout/v3-native-routes"');
    expect(shell).not.toMatch(/const\s+V3_NATIVE_ROUTES\s*=/);
  });

  it("prefix-matches descendants, which is why the obligation spreads", () => {
    expect(isV3Native("/cv")).toBe(true);
    expect(isV3Native("/cv/granska/abc")).toBe(true);
    expect(isV3Native("/cv/importera")).toBe(true);
    // Not a prefix match on a bare string overlap — /cvsomething is a different route.
    expect(isV3Native("/cv-granskning")).toBe(false);
    expect(isV3Native("/installningar")).toBe(false);
  });
});

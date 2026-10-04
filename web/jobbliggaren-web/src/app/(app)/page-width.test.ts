import { readdirSync, readFileSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { stripComments } from "@/lib/layout/strip-comments";

/**
 * Every file that paints into AppShell's `<main>` owns its width (#1062, #1852). The shell gives
 * the content area none: `.jp-content` sets only `flex: 1` and `width: 100%`, so a page without
 * a container renders flush to the viewport edge with no max-width and no page padding —
 * measured on `/cv/granska/[parsedId]` before #1062, 3440px wide at a 3440px viewport.
 *
 * ⚠ **What this test can and cannot see.** It reads source text; it does not render. It
 * therefore proves that a container class is WRITTEN, never that the rendered box is correct —
 * that is the live/E2E measurement's job. It is a regression guard against silent omission,
 * which is the failure that actually happened, and it is deliberately fail-closed: a file it
 * cannot classify fails rather than passes.
 */
const APP = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(APP, "../..");

/** A page owns its width when it renders either shell. Both are defined in globals.css. */
const CONTAINER_CLASSES = ["jp-pagehero", "jp-container"];

type RouteFile = { file: string; url: string; source: string };

/**
 * Every file that paints into the `.jp-content` slot, and therefore inherits the obligation.
 *
 * The set is the **property**, not an enumeration of what happened to be broken. `loading.tsx`
 * earned its place by carrying the identical defect on `/cv/granska/[parsedId]`; `error.tsx`,
 * `not-found.tsx`, `template.tsx` and `default.tsx` paint in the same slot under the same shell
 * rules and are here for the same reason, not because any of them is broken today (measured:
 * `(app)/error.tsx` and `(app)/not-found.tsx` already own `jp-container jp-page`). Guarding only
 * `page.tsx` would have fixed the enumeration and missed the property — the failure mode this
 * repo has paid for before.
 */
const SHELL_PAINTING_FILES = new Set([
  "page.tsx",
  "loading.tsx",
  "error.tsx",
  "not-found.tsx",
  "template.tsx",
  "default.tsx",
]);

/** Every shell-painting file under `(app)`, with the URL it serves. */
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
    if (!SHELL_PAINTING_FILES.has(entry.name)) continue;
    out.push({
      file: full,
      url: "/" + segments.join("/"),
      source: readFileSync(full, "utf-8"),
    });
  }
  return out;
}

/** Every `layout.tsx` below `dir`, outside parallel-route slots and private folders. */
function nestedLayouts(dir: string): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (!entry.isDirectory() || entry.name.startsWith("@") || entry.name.startsWith("_")) continue;
    const child = join(dir, entry.name);
    if (existsSync(join(child, "layout.tsx"))) out.push(join(child, "layout.tsx"));
    out.push(...nestedLayouts(child));
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

const ownsContainer = (source: string) => {
  const code = stripComments(source);
  return CONTAINER_CLASSES.some((c) => code.includes(c));
};

const routeFiles = collectRouteFiles(APP);

describe("(app) — every page owns its width", () => {
  it("finds route files to check (the guard is not vacuously green)", () => {
    // Without this, a broken derivation — a wrong path, a file-name set that matches nothing —
    // would make every assertion below iterate an empty list and report success. A floor,
    // not a pin: new routes must not have to edit this number, and removing routes shrinks
    // the set deliberately, so the failure message has to say which of the two happened
    // rather than leaving a reader with "expected 19 to be >= 20".
    expect(
      routeFiles.length,
      `derived ${routeFiles.length} shell-painting files under (app). If this fell because ` +
        `routes were legitimately removed, lower the floor. If it fell to 0 or a handful, the ` +
        `derivation is broken — the APP path or the file-name set — and every assertion below ` +
        `is passing vacuously.`,
    ).toBeGreaterThanOrEqual(20);
  });

  it("finds no layout below the group's own", () => {
    expect(
      nestedLayouts(APP),
      "this guard reads only the files in SHELL_PAINTING_FILES, never a layout; extend it " +
        "before adding a nested layout.tsx",
    ).toEqual([]);
  });

  it.each(routeFiles.map((f) => [f.url, f] as const))(
    "%s owns a container, delegates to one, or renders nothing",
    (_url, routeFile) => {
      const { file, source } = routeFile;

      const rendersOwnMarkup = stripComments(source).includes("className");

      if (rendersOwnMarkup) {
        expect(
          ownsContainer(source),
          `${file} renders its own markup but contains neither "jp-pagehero" nor ` +
            `"jp-container". AppShell gives no page a width container, so this page renders ` +
            `edge-to-edge at every viewport. Wrap it the way the (app) standard does: a ` +
            `.jp-pagehero band, then .jp-container.jp-page.`,
        ).toBe(true);
        return;
      }

      // Renders no markup of its own: either it delegates to a component that owns the
      // shell, or it is a gate that renders nothing at all. Follow one hop before
      // concluding anything.
      //
      // Only `@/components/…` counts as delegation — narrowed after the first run, because
      // matching every `@/…` import classified the six deferred-feature 404 stubs as
      // delegating purely for importing `@/lib/auth/session`. That narrowing is an allowlist
      // on import prefix; the assertion below the delegation branch is what stops it becoming
      // a hole. Delegation is checked BEFORE the gate on purpose: a page that both redirects
      // on auth AND renders a component must be judged on
      // the component, or the gate branch would let it through unchecked.
      //
      // `delegates.some(...)` accepts ANY imported component, not necessarily the one
      // rendered. That is deliberate slack: resolving which component is rendered needs a
      // parser, and the over-accept direction only ever lets a page through that imports a
      // shell-owning component — a far narrower miss than the under-reach it replaces.
      const imports = [...source.matchAll(/from\s+"(@\/components\/[^"]+)"/g)].map(
        (m) => m[1]!,
      );
      const delegates = imports
        .map(resolveLocalImport)
        .filter((p): p is string => p !== null)
        .map((p) => readFileSync(p, "utf-8"));
      const unresolved = imports.filter((spec) => resolveLocalImport(spec) === null);
      expect(
        unresolved,
        `${file}: resolveLocalImport cannot follow ${unresolved.join(", ")}. Teach it the form.`,
      ).toEqual([]);

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

      // Before the gate branch: is there a delegation this guard simply cannot follow?
      //
      // The gate branch below is a TERMINAL PASS, and `redirect(` is near-universal in this
      // route group (`if (!user) redirect("/logga-in")` is the session gate on almost every
      // page). So a file that renders no markup, delegates through a form the extractor above
      // does not read, and carries a session gate would sail through **unexamined**. Measured:
      // no such file exists today — zero relative imports and zero `next/dynamic` across the
      // derived set — but "no instance today" is not a guarantee, and the `@/components/…`
      // narrowing above IS an allowlist on import prefix, whatever its motivation.
      const unfollowable =
        /from\s+"\.{1,2}\//.test(source) || /next\/dynamic/.test(source);
      expect(
        unfollowable,
        `${file} renders no markup and delegates through an import this guard cannot ` +
          `follow (a relative import, or next/dynamic). The gate branch below would pass it ` +
          `unexamined because almost every page in this group calls redirect() for its ` +
          `session gate. Teach resolveLocalImport, or import the component through @/.`,
      ).toBe(false);

      // Neither markup nor a followable component. The only legitimate remaining shape is a
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

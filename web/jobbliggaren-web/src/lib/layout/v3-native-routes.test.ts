import { readdirSync, readFileSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { V3_NATIVE_ROUTES, isV3Native } from "./v3-native-routes";

/**
 * Freezes the obligation `V3_NATIVE_ROUTES` creates (#1062). What that obligation is, and the
 * two pages that had drifted before anyone measured, is in the module's own docblock — one
 * home for the incident, not two.
 *
 * ⚠ **What this test can and cannot see.** It reads source text; it does not render. It
 * therefore proves that a container class is WRITTEN, never that the rendered box is correct —
 * that is the live/E2E measurement's job. It is a regression guard against silent omission,
 * which is the failure that actually happened, and it is deliberately fail-closed: a file it
 * cannot classify fails rather than passes.
 */
const APP = resolve(dirname(fileURLToPath(import.meta.url)), "../../app/(app)");
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), "../..");

/** A page owns its width when it renders either shell. Both are defined in globals.css. */
const CONTAINER_CLASSES = ["jp-pagehero", "jp-container"];

/**
 * Source with comments removed, string literals preserved.
 *
 * ⚠ **This is the load-bearing part of the guard, and its absence made the first revision
 * fail-open on the very file this PR rebuilt.** `cv/[id]/granska/page.tsx` gained a docblock naming
 * `jp-pagehero` and `jp-container jp-page` in prose, so a plain `source.includes()` was satisfied by
 * the comment: with every container class deleted from its markup, the guard still reported green.
 * The mutation proof missed it because it had picked `/cv/importera`, whose docblock happens not to
 * name the classes — a proof that passed while the hole stood open two files away.
 *
 * Reading `className="…"` attributes instead was tried and rejected: it cannot see
 * `className={wrapperClass}`, which is how `PlainHeaderSkeleton` (the delegate behind
 * `/ny-ansokan/loading.tsx`) genuinely owns its container. That form is real and correct, so an
 * extractor that cannot read it produces false failures rather than safety.
 *
 * The scanner tracks quotes so a `//` inside `href="https://…"` is not mistaken for a comment —
 * which would truncate the rest of the line and could drop a real class.
 */
function stripComments(source: string): string {
  let out = "";
  let quote: string | null = null;
  for (let i = 0; i < source.length; i++) {
    const c = source[i]!;
    if (quote) {
      if (c === "\\") { out += c + (source[i + 1] ?? ""); i++; continue; }
      if (c === quote) quote = null;
      out += c;
      continue;
    }
    if (c === '"' || c === "'" || c === "`") { quote = c; out += c; continue; }
    if (c === "/" && source[i + 1] === "*") {
      const end = source.indexOf("*/", i + 2);
      i = end === -1 ? source.length : end + 1;
      continue;
    }
    if (c === "/" && source[i + 1] === "/") {
      const end = source.indexOf("\n", i);
      i = end === -1 ? source.length : end - 1;
      continue;
    }
    out += c;
  }
  return out;
}

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

/** Every `.ts`/`.tsx` file under `src/`, for the single-declaration sweep below. */
function collectSourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      collectSourceFiles(full, acc);
    } else if (/\.tsx?$/.test(entry.name)) {
      acc.push(full);
    }
  }
  return acc;
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

const routeFiles = collectRouteFiles(APP).filter((f) => isV3Native(f.url));

describe("V3_NATIVE_ROUTES — every page under a v3-native prefix owns its width", () => {
  it("finds route files to check (the guard is not vacuously green)", () => {
    // Without this, a broken derivation — a wrong path, a filter that matches nothing —
    // would make every assertion below iterate an empty list and report success. A floor,
    // not a pin: new routes must not have to edit this number, and the module's own REMOVAL
    // TRIGGER shrinks the list deliberately, so the failure message has to say which of the
    // two happened rather than leaving a reader with "expected 19 to be >= 20".
    expect(
      routeFiles.length,
      `derived ${routeFiles.length} shell-painting files under a v3-native prefix. If this ` +
        `fell because routes were legitimately removed (see V3_NATIVE_ROUTES' REMOVAL ` +
        `TRIGGER), lower the floor. If it fell to 0 or a handful, the derivation is broken ` +
        `— APP path, the file-name set, or the prefix filter — and every assertion below is ` +
        `passing vacuously.`,
    ).toBeGreaterThanOrEqual(20);
    expect(V3_NATIVE_ROUTES.length).toBeGreaterThan(0);
  });

  it.each(routeFiles.map((f) => [f.url, f] as const))(
    "%s owns a container, delegates to one, or renders nothing",
    (_url, routeFile) => {
      const { file, source } = routeFile;

      const rendersOwnMarkup = stripComments(source).includes("className");

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
      // concluding anything.
      //
      // Only `@/components/…` counts as delegation — narrowed after the first run, because
      // matching every `@/…` import classified the six deferred-feature 404 stubs as
      // delegating purely for importing `@/lib/auth/session`. That narrowing is an allowlist
      // on import prefix; the assertion below the delegation branch is what stops it becoming
      // a hole. Delegation is checked BEFORE the gate on purpose: a page that both redirects
      // on auth AND renders a component (`/oversikt` is the only one today) must be judged on
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

describe("V3_NATIVE_ROUTES — the list itself", () => {
  it("has one home, and app-shell USES it rather than merely importing it", () => {
    // The list was inlined in app-shell.tsx until #1062. A second copy would let the shell
    // and this guard disagree about which routes carry the obligation — the guard would then
    // pass while the shell opted a page out.
    const shell = readFileSync(resolve(SRC, "components/shell/app-shell.tsx"), "utf-8");
    expect(shell).toContain('from "@/lib/layout/v3-native-routes"');
    // An import is not a use. Asserting only the import would stay green if the call site
    // were replaced by an inline condition and the import left behind as a dead line.
    expect(shell).toMatch(/isV3Native\s*\(/);
  });

  it("is declared in exactly one file across src/", () => {
    // Name-based and deliberately broad. The earlier form read app-shell.tsx alone and
    // matched only `const`, so it could not see `let`/`var`, a copy in any other file, or —
    // the case that actually occurred in this PR — a stale POINTER in globals.css still
    // naming app-shell as the list's home. Sweeping every source file for the identifier
    // catches a second declaration wherever it lands.
    const declarations = collectSourceFiles(SRC).filter(
      (f) =>
        !f.endsWith("v3-native-routes.ts") &&
        !f.endsWith("v3-native-routes.test.ts") &&
        /(const|let|var)\s+V3_NATIVE_ROUTES\s*=/.test(readFileSync(f, "utf-8")),
    );
    expect(
      declarations,
      `V3_NATIVE_ROUTES is declared outside its module. One home, or the shell and this ` +
        `guard can disagree about which routes carry the container obligation.`,
    ).toEqual([]);
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

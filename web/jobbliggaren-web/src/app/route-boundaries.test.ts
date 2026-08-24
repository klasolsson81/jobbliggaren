import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";

/**
 * Fitness function for the route-level failure boundaries (#1477).
 *
 * The defect it closes: `(auth)`, `(marketing)`, `(marketing-inner)`,
 * `(guest)` and `(admin)` had no `error.tsx` at all, so a throw anywhere in
 * them bubbled past every boundary to `global-error.tsx` — which by Next
 * convention REPLACES the root layout, and therefore renders with no header, no
 * footer and no way back. Only `(app)` had ever got the file. Klas, 2026-08-23:
 * *"Man får aldrig aldrig hamna utanför jobbliggarens ramverk."*
 *
 * Both rules are computed from the FILESYSTEM, never from a list. A list is a
 * silent hole — the next route group would join it by being forgotten, which is
 * exactly how the five above came to be missing.
 */

const APP_ROOT = dirname(fileURLToPath(import.meta.url));
const toPosix = (p: string) => p.split(sep).join("/");
const isTestFile = (name: string) => /\.(test|spec)\.(ts|tsx)$/.test(name);

function directories(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (!entry.isDirectory()) continue;
    const child = resolve(dir, entry.name);
    acc.push(child);
    directories(child, acc);
  }
  return acc;
}

function exists(file: string): boolean {
  try {
    return statSync(file).isFile();
  } catch {
    return false;
  }
}

/** Walk a parsed source for a real, zero-argument `notFound()` call. */
function hasNotFoundCall(sourceFile: ts.SourceFile): boolean {
  let found = false;
  const visit = (node: ts.Node): void => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === "notFound" &&
      node.arguments.length === 0
    ) {
      found = true;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return found;
}

function parse(file: string, text: string): ts.SourceFile {
  return ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    /* setParentNodes — forEachChild only */ false,
    file.endsWith(".ts") ? ts.ScriptKind.TS : ts.ScriptKind.TSX
  );
}

/**
 * True iff the file really calls `notFound()`.
 *
 * PARSED, not grepped, and the difference is not stylistic. Removing comments
 * with a regex is fail-OPEN in a way that hides call sites: block comments must
 * be stripped before line comments, so a slash-star sequence sitting INSIDE a
 * line comment opens a block that runs to the next star-slash. This repo writes
 * route globs in exactly that position — `(guest)/gast/layout.tsx` documents the
 * guest glob in a line comment — and that one comment swallowed most of that
 * file, `export default` included. A file whose docblock carried such
 * a glob above its `notFound()` would drop out of the scan, and the rule below
 * would then pass against a smaller world than the real one. Comments are not
 * part of the AST, so this cannot happen here.
 */
function callsNotFound(file: string): boolean {
  return hasNotFoundCall(parse(file, readFileSync(file, "utf8")));
}

/** Same walk over a source string — used by the scanner's own controls. */
function callsNotFoundIn(source: string): boolean {
  return hasNotFoundCall(parse("probe.tsx", source));
}

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = resolve(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(child, acc);
    else if (/\.tsx?$/.test(entry.name) && !isTestFile(entry.name)) acc.push(child);
  }
  return acc;
}

describe("route-level failure boundaries (#1477)", () => {
  it("every segment that owns a layout owns an error boundary", () => {
    // A segment's error.tsx renders as ITS layout's children, which is the only
    // placement where the chrome that layout draws survives the throw. So the
    // rule keys on layout ownership, not on route groups: `(guest)`'s layout is
    // at `gast/`, and a boundary one level up would render outside the shell.
    // The app root is exempt — a throw in the root layout is what
    // global-error.tsx is for, and a segment's error.tsx cannot catch its own
    // layout.
    const owners = directories(APP_ROOT).filter((d) => exists(resolve(d, "layout.tsx")));

    expect(
      owners.length,
      "no layout-owning segment found — the walk is broken, so the rule below is vacuous"
    ).toBeGreaterThanOrEqual(6);

    const missing = owners
      .filter((d) => !exists(resolve(d, "error.tsx")))
      .map((d) => toPosix(relative(APP_ROOT, d)));

    expect(
      missing,
      "these segments draw chrome from a layout but have no error.tsx, so a throw " +
        "in them renders global-error.tsx — outside the site frame, with no way back"
    ).toEqual([]);
  });

  it("the app root keeps its own last-resort boundaries", () => {
    expect(exists(resolve(APP_ROOT, "global-error.tsx"))).toBe(true);
    expect(exists(resolve(APP_ROOT, "not-found.tsx"))).toBe(true);
  });

  it("the scanner counts notFound() CALLS, not mentions of them", () => {
    expect(callsNotFoundIn('import { notFound } from "next/navigation";\nnotFound();')).toBe(true);
    expect(callsNotFoundIn("// the retired stub answers with notFound(), not a redirect\nexport {};")).toBe(false);
    expect(callsNotFoundIn("/** answers notFound() when the id is unknown */\nexport {};")).toBe(false);
    expect(
      callsNotFoundIn(
        "// middleware does not list `/gast/*` as protected\nnotFound();\n/** trailing doc */\nexport {};"
      )
    ).toBe(true);
  });

  it("every notFound() caller is covered by a not-found boundary inside its own shell", () => {
    // Falling through to the ROOT not-found is not coverage: it renders the
    // PUBLIC marketing frame, which is the wrong shell for a signed-in page or
    // for a visitor inside guest mode. So an ancestor other than the app root
    // must carry the file.
    const callers = sourceFiles(APP_ROOT).filter(callsNotFound);

    expect(
      callers.length,
      "far fewer notFound() call sites than this tree has — the scan looks collapsed, " +
        "so the rule below is checked against a smaller world than the real one"
    ).toBeGreaterThanOrEqual(10);

    const uncovered = callers
      .filter((file) => {
        for (let dir = dirname(file); dir !== APP_ROOT; dir = dirname(dir)) {
          if (exists(resolve(dir, "not-found.tsx"))) return false;
        }
        return true;
      })
      .map((f) => toPosix(relative(APP_ROOT, f)));

    expect(
      uncovered,
      "these call notFound() with no not-found.tsx above them inside their own " +
        "route group, so they fall through to the root 404 and its public frame"
    ).toEqual([]);
  });
});

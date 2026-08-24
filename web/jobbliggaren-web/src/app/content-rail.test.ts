import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";

/**
 * Fitness function for the content rail (DESIGN.md's content-width canon:
 * header = plate = content).
 *
 * The defect it closes, measured on PR #1495 and repaired in the same PR as
 * this file: `.jp-container` centres with `margin-inline: auto`, and a flex
 * item with auto inline margins gets no `stretch`. So a centring container that
 * is also a flex item collapses to fit-content unless something else gives it a
 * width — silently, with no warning anywhere. Two surfaces shipped that way and
 * neither test nor type nor lint could see it: the root 404 and the marketing
 * error boundary, whose headings sat hundreds of pixels right of the brand at
 * every desktop viewport.
 *
 * The rule is the full condition, not a proxy for it:
 *
 *     centring container  AND  flex item  AND  no definite inline size
 *
 * Getting that third term wrong is how a guard ends up inert. A rule keyed on
 * `flex-1` alone would pass the very repair this PR ships — the fix moves
 * `flex-1` to a wrapper and keeps `<main>` a flex item with `w-full`, so
 * deleting `w-full` would restore the defect with no `flex-1` anywhere for such
 * a rule to see. Both remedies are therefore recognised: leave the flex-item
 * role (no flex parent), or keep it and carry a definite width.
 *
 * The centring classes are read from the STYLESHEET, never from a list here. A
 * list is what goes stale: the next class to gain `margin-inline: auto` would
 * join it by being forgotten, which is the same way the two defects above came
 * to exist.
 *
 * Known reach, and the list is not short. Flex-item-ness is decided from the
 * enclosing JSX element IN THE SAME FILE, so an element whose parent is a
 * layout's `{children}` is only caught by the `flex-*` it carries itself. Class
 * names composed at runtime — a template literal's interpolated half, a variable
 * holding the token — are invisible to any static scan; `scripts/guard-css.mjs`
 * declares the same limit for the same reason. And the container side is a token
 * list, not a measurement like the centring side: a parent that becomes a flex or
 * grid container through a `.jp-*` class or a responsive variant
 * is not modelled. Measured when this was written — widening it to grid changed
 * nothing, and no stylesheet class that is currently the parent of a centring
 * element is a flex or grid container — so the gap is declared rather than closed.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(HERE, "..");
const STYLESHEETS = [resolve(HERE, "globals.css"), resolve(HERE, "(app)", "app.css")];

const toPosix = (p: string) => p.split(sep).join("/");

/**
 * Tailwind tokens that make an element a formatting context whose children are
 * items. Grid is here because auto inline margins disarm `justify-self: stretch`
 * exactly as they disarm the flex one.
 */
const FLEX_CONTAINER = new Set(["flex", "inline-flex", "grid", "inline-grid"]);

/** `flex-<n>` and friends: written only on something the author means as a flex item. */
const FLEX_ITEM_INTENT = /^flex-(1|auto|initial|none|\[)/;

/** `w-auto`/`w-fit`/`w-min`/`w-max` size to content — they do NOT rescue the box. */
const INDEFINITE_WIDTH = new Set(["w-auto", "w-fit", "w-min", "w-max"]);

/**
 * Class names whose own rule centres the box with `margin-inline: auto`.
 *
 * Matches a rule that starts at column 0 — every such declaration in these
 * stylesheets is top level — and reads only its own braces, so a nested @media
 * block cannot lend its selector to the rule inside it.
 */
function centringClasses(css: string): Set<string> {
  const found = new Set<string>();
  for (const m of css.matchAll(/^(\.[^{}\n][^{}]*?)\{([^{}]*)\}/gm)) {
    const [, selector = "", body = ""] = m;
    if (!/margin-inline:\s*auto\b/.test(body)) continue;
    for (const c of selector.matchAll(/\.([A-Za-z][A-Za-z0-9_-]*)/g)) {
      if (c[1]) found.add(c[1]);
    }
  }
  return found;
}

type Element = { own: Set<string>; parent: Set<string> | null; line: number };

/**
 * Every JSX element with its own class tokens and its enclosing element's.
 *
 * Tokens are gathered from the whole `className` initializer, so
 * `cn("a", "b")` reads as one element's classes rather than two unrelated
 * strings. Fragments render no box, so they pass the enclosing element through
 * rather than becoming one.
 */
function scanElements(file: string, text: string): Element[] {
  const source = ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    /* setParentNodes — the walk carries parents itself */ false,
    file.endsWith(".ts") ? ts.ScriptKind.TS : ts.ScriptKind.TSX
  );
  const out: Element[] = [];

  const literals = (node: ts.Node, into: Set<string>): void => {
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
      for (const t of node.text.split(/\s+/)) if (t) into.add(t);
      return;
    }
    if (ts.isTemplateExpression(node)) {
      for (const t of node.head.text.split(/\s+/)) if (t) into.add(t);
      for (const span of node.templateSpans) {
        for (const t of span.literal.text.split(/\s+/)) if (t) into.add(t);
        literals(span.expression, into);
      }
      return;
    }
    ts.forEachChild(node, (c) => literals(c, into));
  };

  const classesOf = (attrs: ts.JsxAttributes): Set<string> => {
    const into = new Set<string>();
    for (const a of attrs.properties) {
      if (ts.isJsxAttribute(a) && ts.isIdentifier(a.name) && a.name.text === "className" && a.initializer) {
        literals(a.initializer, into);
      }
    }
    return into;
  };

  const at = (node: ts.Node) =>
    source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;

  const walk = (node: ts.Node, parent: Set<string> | null): void => {
    if (ts.isJsxElement(node)) {
      const own = classesOf(node.openingElement.attributes);
      out.push({ own, parent, line: at(node) });
      for (const child of node.children) walk(child, own);
      // Attribute values may hold JSX of their own; that JSX is not this
      // element's child in the rendered tree, so it inherits no parent here.
      for (const a of node.openingElement.attributes.properties) walk(a, null);
      return;
    }
    if (ts.isJsxSelfClosingElement(node)) {
      const own = classesOf(node.attributes);
      out.push({ own, parent, line: at(node) });
      for (const a of node.attributes.properties) walk(a, null);
      return;
    }
    if (ts.isJsxFragment(node)) {
      for (const child of node.children) walk(child, parent);
      return;
    }
    ts.forEachChild(node, (n) => walk(n, parent));
  };

  walk(source, null);
  return out;
}

/** The condition itself. Returns the offending centring class, or null. */
function collapsingContainer(el: Element, centring: Set<string>): string | null {
  const container = [...el.own].find((c) => centring.has(c));
  if (!container) return null;

  const isFlexItem =
    [...el.own].some((c) => FLEX_ITEM_INTENT.test(c)) ||
    (el.parent !== null && [...el.parent].some((c) => FLEX_CONTAINER.has(c)));
  if (!isFlexItem) return null;

  const hasDefiniteWidth = [...el.own].some(
    (c) => /^w-/.test(c) && !INDEFINITE_WIDTH.has(c)
  );
  return hasDefiniteWidth ? null : container;
}

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = resolve(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(child, acc);
    else if (/\.tsx$/.test(entry.name) && !/\.(test|spec)\.tsx$/.test(entry.name)) acc.push(child);
  }
  return acc;
}

/** Run the whole rule over one source string — used by the scanner's own controls. */
function offendersIn(source: string, centring: Set<string>): string[] {
  return scanElements("probe.tsx", source)
    .map((el) => collapsingContainer(el, centring))
    .filter((c): c is string => c !== null);
}

describe("content rail — a centring container is never a flex item without a width", () => {
  const css = STYLESHEETS.filter((f) => {
    try {
      return statSync(f).isFile();
    } catch {
      return false;
    }
  })
    .map((f) => readFileSync(f, "utf8"))
    .join("\n");

  const centring = centringClasses(css);

  it("reads the centring classes out of the stylesheet", () => {
    // Non-vacuity. If the scan collapses, every rule below passes against an
    // empty world and reports success for a repo it never read.
    expect(
      centring.size,
      "no centring class found — the stylesheet scan is broken, so the rule below is vacuous"
    ).toBeGreaterThanOrEqual(8);
    expect(centring.has("jp-container")).toBe(true);
    expect(centring.has("jp-shell-transitional-container")).toBe(true);
    // A class that centres nothing must not be swept in.
    expect(centring.has("jp-h1")).toBe(false);
  });

  it("the stylesheet scan reads a rule's OWN braces", () => {
    // Both halves matter. Missing a real declaration is fail-open; borrowing a
    // selector across braces would invent classes that never centre anything.
    expect([...centringClasses(".probe { margin-inline: auto; }")]).toEqual(["probe"]);
    expect([...centringClasses(".probe { margin-inline: 0 auto; }")]).toEqual([]);
    expect([...centringClasses(".outer { color: red; }\n.inner { margin-inline: auto; }")]).toEqual([
      "inner",
    ]);
    expect(
      [...centringClasses("@media (min-width: 720px) {\n  .nested { margin-inline: auto; }\n}")]
    ).toEqual([]);
  });

  it("catches BOTH shapes of the defect, and clears both remedies", () => {
    const c = new Set(["jp-container"]);

    // Shape 1 — what shipped: the container declares itself a flex item.
    expect(offendersIn('const a = <div className="jp-container flex-1" />;', c)).toEqual([
      "jp-container",
    ]);

    // Shape 2 — the regression this PR's own repair could suffer: no `flex-1`
    // anywhere, the parent makes it an item, and the width has been deleted.
    expect(
      offendersIn(
        'const a = <div className="flex flex-1 flex-col"><main className="jp-container flex flex-col" /></div>;',
        c
      )
    ).toEqual(["jp-container"]);

    // Remedy A — not a flex item at all (block spacer between).
    expect(
      offendersIn(
        'const a = <div className="flex flex-col"><div className="flex-1"><main className="jp-container" /></div></div>;',
        c
      )
    ).toEqual([]);

    // Remedy B — still an item, but with a definite width. This is what ships.
    expect(
      offendersIn(
        'const a = <div className="flex flex-1 flex-col"><main className="jp-container w-full" /></div>;',
        c
      )
    ).toEqual([]);

    // A width that sizes to content rescues nothing, and must not read as one.
    expect(
      offendersIn(
        'const a = <div className="flex flex-col"><main className="jp-container w-fit" /></div>;',
        c
      )
    ).toEqual(["jp-container"]);
  });

  it("reads the element tree, not the string list", () => {
    const c = new Set(["jp-container"]);

    // Two literals, one element. Reading literals independently would report
    // each as clean and the pair as absent.
    expect(offendersIn('const a = <div className={cn("jp-container", "flex-1")} />;', c)).toEqual([
      "jp-container",
    ]);

    // Two separate elements must stay separate.
    expect(
      offendersIn('const a = <><div className="jp-container" /><span className="flex-1" /></>;', c)
    ).toEqual([]);

    // A fragment renders no box, so it must pass the flex parent THROUGH.
    expect(
      offendersIn(
        'const a = <div className="flex flex-col"><><main className="jp-container" /></></div>;',
        c
      )
    ).toEqual(["jp-container"]);

    // A non-flex parent makes it no item, so the container is fine bare.
    expect(
      offendersIn('const a = <div className="block"><main className="jp-container" /></div>;', c)
    ).toEqual([]);
  });

  it("no centring container in the tree collapses", () => {
    const files = sourceFiles(SRC);
    const seen = files.map((f) => toPosix(relative(SRC, f)));
    const subtrees = [...new Set(seen.map((p) => p.split("/")[0]))];

    // Non-vacuity, keyed on what the walk REACHED rather than on how much of it.
    // A count cannot see the collapse that matters, because either subtree alone
    // clears any threshold low enough to be safe: dropping `app/` loses every
    // route surface including both files the defect shipped on. So both are
    // asserted, and the two repaired surfaces by name on top.
    expect(subtrees, "the walk no longer reaches one of the two subtrees").toEqual(
      expect.arrayContaining(["app", "components"])
    );
    expect(seen, "the walk no longer reaches the surfaces this rule was written for").toEqual(
      expect.arrayContaining(["app/not-found.tsx", "app/(marketing)/error.tsx"])
    );

    const offenders: string[] = [];
    for (const file of files) {
      for (const el of scanElements(file, readFileSync(file, "utf8"))) {
        const c = collapsingContainer(el, centring);
        if (c) offenders.push(`${toPosix(relative(SRC, file))}:${el.line} — ${c}`);
      }
    }

    expect(
      offenders,
      "a flex item with auto inline margins gets no stretch, so these centring containers " +
        "collapse to fit-content and never apply the content rail — the heading lands " +
        "hundreds of pixels right of the brand. Either take away the flex-item role (put " +
        "the flex-1 on a wrapper), or keep it and give the element a definite width."
    ).toEqual([]);
  });
});

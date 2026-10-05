import { readdirSync, readFileSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";
import { readRules, stripComments } from "@/test/css-rules";

/**
 * #1875 — a list row's column template lives in the stylesheet, never in an inline style.
 *
 * The row chassis `.jp-job, .jp-app` (`globals.css`) is a grid whose <=720px arm drops `.jp-job` to
 * one column, so the actions fall under the text. An inline `style` outranks every stylesheet rule,
 * so a row that pinned its columns inline kept them at 320px and its button or match chip painted
 * over the title — on /sokningar, /matchningar and /sparade. Nothing could see it: jsdom applies no
 * stylesheet, and `guard:css` never reads `style=`.
 *
 * Two halves of one contract, both computed from the source tree and the stylesheets, never from a
 * list:
 *   1. no chassis element (`jp-job`, `jp-app`, or one of their `__parts`) carries an inline grid
 *      property — and a `style` or spread the scan cannot read counts against it (fail closed);
 *   2. a `.jp-job--*` modifier that sets a grid template comes BEFORE the <=720px arm. A media query
 *      adds no specificity, so source order alone decides (see `globals-pagehero-cascade.test.ts`);
 *      and no later stylesheet sets a grid on the row.
 *
 * Known reach: a className held in a variable, composed at runtime, created with `createElement`, or
 * kept as a constant in a `.ts` file is invisible to a static scan; `scripts/guard-css.mjs` declares
 * the same limit.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(HERE, "..");
const GLOBALS = readFileSync(resolve(HERE, "globals.css"), "utf8");
const LATER_SHEETS = [resolve(HERE, "(app)", "app.css"), resolve(HERE, "(admin)", "admin.css")];

const toPosix = (p: string) => p.split(sep).join("/");

/** The chassis blocks and their parts. `jp-jobs`, `jp-job-tags` and `jp-job-skeleton` are other blocks. */
const isChassis = (token: string) => /^jp-(job|app)(__[a-z0-9-]+)?$/.test(token);

/** `gridTemplateColumns`, `"grid-template-columns"` and `gridAutoFlow` all name a grid property. */
const isGridKey = (key: string) => key.replace(/-/g, "").toLowerCase().startsWith("grid");

type Finding = { line: number; why: string };

function scan(file: string, text: string): { chassis: number; findings: Finding[] } {
  const source = ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    /* setParentNodes — getStart(source) is passed explicitly */ false,
    ts.ScriptKind.TSX
  );
  const at = (node: ts.Node) => source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;

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

  let chassis = 0;
  const findings: Finding[] = [];

  const check = (attrs: ts.JsxAttributes) => {
    const classes = new Set<string>();
    let style: ts.JsxAttribute | undefined;
    const spreads: ts.JsxSpreadAttribute[] = [];
    for (const a of attrs.properties) {
      if (ts.isJsxSpreadAttribute(a)) {
        spreads.push(a);
        continue;
      }
      if (!ts.isIdentifier(a.name)) continue;
      if (a.name.text === "className" && a.initializer) literals(a.initializer, classes);
      if (a.name.text === "style") style = a;
    }
    if (![...classes].some(isChassis)) return;
    chassis++;
    for (const s of spreads) findings.push({ line: at(s), why: "spread attribute" });
    if (!style) return;

    const value =
      style.initializer && ts.isJsxExpression(style.initializer) ? style.initializer.expression : undefined;
    if (!value || !ts.isObjectLiteralExpression(value)) {
      findings.push({ line: at(style), why: "style is not an object literal" });
      return;
    }
    for (const prop of value.properties) {
      if (ts.isSpreadAssignment(prop)) {
        findings.push({ line: at(prop), why: "spread inside style" });
        continue;
      }
      const name = prop.name;
      if (!name || ts.isComputedPropertyName(name)) {
        findings.push({ line: at(prop), why: "computed style key" });
        continue;
      }
      const key = ts.isIdentifier(name) || ts.isStringLiteral(name) ? name.text : name.getText(source);
      if (isGridKey(key)) findings.push({ line: at(prop), why: key });
    }
  };

  const walk = (node: ts.Node): void => {
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) check(node.attributes);
    ts.forEachChild(node, walk);
  };
  walk(source);
  return { chassis, findings };
}

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = resolve(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(child, acc);
    else if (/\.tsx$/.test(entry.name) && !/\.(test|spec)\.tsx$/.test(entry.name)) acc.push(child);
  }
  return acc;
}

/** Each `.jp-job--*` modifier that sets a grid template but sits after the <=720px arm. */
function modifiersAfterTheArm(css: string): string[] {
  const rules = readRules(css);
  const arm = rules.find(
    (r) => r.selector === ".jp-job" && r.inMedia?.includes("max-width: 720px") && r.properties.includes("grid-template-columns")
  );
  if (!arm) return ["no <=720px `.jp-job` arm"];
  return rules
    .filter((r) => !r.inMedia && /^\.jp-job--[a-z0-9-]+$/.test(r.selector) && r.properties.some(isGridKey))
    .filter((r) => r.start > arm.start)
    .map((r) => r.selector);
}

/** Every value `property` is declared with, in every block whose selector list names `selector`. */
function declared(css: string, selector: string, property: string): string[] {
  return [...stripComments(css).matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    .filter((m) =>
      ((m[1] ?? "").split(";").at(-1) ?? "")
        .split(",")
        .map((s) => s.trim().replace(/\s+/g, " "))
        .includes(selector)
    )
    .flatMap((m) =>
      [...(m[2] ?? "").matchAll(new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`, "g"))].map((v) =>
        (v[1] ?? "").trim()
      )
    );
}

const findingsIn = (source: string) => scan("probe.tsx", source).findings.map((f) => f.why);

describe("a list row's columns live in the stylesheet (#1875)", () => {
  it("reads every spelling of a grid property, and fails closed on what it cannot read", () => {
    expect(findingsIn('const a = <article className="jp-job" style={{ gridTemplateColumns: "1fr auto" }} />;')).toEqual([
      "gridTemplateColumns",
    ]);
    expect(findingsIn('const a = <article className={cn("jp-job", x)} style={{ gridTemplateColumns: "1fr" }} />;')).toEqual([
      "gridTemplateColumns",
    ]);
    expect(findingsIn('const a = <article className={`jp-job ${x}`} style={{ "grid-template-columns": "1fr" }} />;')).toEqual([
      "grid-template-columns",
    ]);
    expect(findingsIn('const a = <article className="jp-app" style={{ gridAutoFlow: "column" }} />;')).toEqual([
      "gridAutoFlow",
    ]);
    expect(findingsIn('const a = <div className="jp-job__actions" style={{ gridColumn: 2 }} />;')).toEqual(["gridColumn"]);
    expect(findingsIn('const a = <article className="jp-job" style={s} />;')).toEqual(["style is not an object literal"]);
    expect(findingsIn('const a = <article className="jp-job" style={{ ...s }} />;')).toEqual(["spread inside style"]);
    expect(findingsIn('const a = <article className="jp-job" style={{ [k]: "1fr" }} />;')).toEqual(["computed style key"]);
    expect(findingsIn('const a = <article className="jp-job" {...rest} />;')).toEqual(["spread attribute"]);
  });

  it("names the chassis by its exact tokens, and reads elements, not mentions", () => {
    expect(findingsIn('const a = <ul className="jp-jobs" style={{ gridTemplateColumns: "1fr" }} />;')).toEqual([]);
    expect(findingsIn('const a = <div className="jp-job-skeleton" style={{ gridTemplateColumns: "1fr" }} />;')).toEqual([]);
    expect(findingsIn('const a = <article className="jp-job" style={{ opacity: 0.7 }} />;')).toEqual([]);
    expect(
      findingsIn(
        '// <article className="jp-job" style={{ gridTemplateColumns: "1fr" }} />\n' +
          "const s = '<article className=\"jp-job\" style={{ gridTemplateColumns: \"1fr\" }} />';"
      )
    ).toEqual([]);
  });

  it("no chassis element in the tree carries an inline grid property", () => {
    const files = sourceFiles(SRC);
    const seen = files.map((f) => toPosix(relative(SRC, f)));

    // Non-vacuity, keyed on what the walk REACHED: both subtrees, and the four rows this rule was
    // written for (canaries of reach, not its scope).
    expect([...new Set(seen.map((p) => p.split("/")[0]))], "the walk no longer reaches a subtree").toEqual(
      expect.arrayContaining(["app", "components"])
    );
    expect(seen, "the walk no longer reaches the rows this rule was written for").toEqual(
      expect.arrayContaining([
        "components/recent-searches/recent-search-row.tsx",
        "components/saved-job-ads/saved-job-ad-row.tsx",
        "components/matches/match-list.tsx",
        "components/application-history/application-history-employer-card.tsx",
      ])
    );

    let chassis = 0;
    const offenders: string[] = [];
    for (const file of files) {
      const rel = toPosix(relative(SRC, file));
      const result = scan(file, readFileSync(file, "utf8"));
      chassis += result.chassis;
      if (rel === "components/saved-job-ads/saved-job-ad-row.tsx") {
        expect(result.chassis, "saved-job-ad-row renders a row in both branches").toBeGreaterThanOrEqual(2);
      }
      for (const f of result.findings) offenders.push(`${rel}:${f.line} — ${f.why}`);
    }
    expect(chassis, "the scan found almost no chassis elements, so the rule below is vacuous").toBeGreaterThanOrEqual(10);

    expect(
      offenders,
      "an inline grid property outranks every stylesheet rule, so the row keeps its columns below " +
        "720px and the actions paint over the title. Give the row a `.jp-job--*` modifier in " +
        "globals.css, placed before the <=720px arm, instead."
    ).toEqual([]);
  });

  it("reports a modifier placed after the <=720px arm, and not one placed before it", () => {
    const base = ".jp-job, .jp-app { display: grid; grid-template-columns: 1fr auto; }\n";
    const arm = "@media (max-width: 720px) { .jp-job { grid-template-columns: 1fr; } }\n";
    const modifier = ".jp-job--probe { grid-template-columns: auto 1fr auto; }\n";
    expect(modifiersAfterTheArm(base + arm + modifier)).toEqual([".jp-job--probe"]);
    expect(modifiersAfterTheArm(base + modifier + arm)).toEqual([]);
  });

  it("every `.jp-job` modifier that sets columns precedes the <=720px arm, and no later sheet sets them", () => {
    expect(
      modifiersAfterTheArm(GLOBALS),
      "a media query adds no specificity: a `.jp-job--*` grid template placed after the <=720px arm " +
        "wins at every width, so the row never drops to one column. Move the modifier above the arm."
    ).toEqual([]);
    for (const sheet of LATER_SHEETS) {
      const late = readRules(readFileSync(sheet, "utf8")).filter(
        (r) =>
          (r.selector.match(/\.[\w-]+/g) ?? []).some((c) => /^\.jp-job(__[a-z0-9-]+|--[a-z0-9-]+)*$/.test(c)) &&
          r.properties.some(isGridKey)
      );
      expect(late.map((r) => r.selector), `${toPosix(relative(SRC, sheet))} loads after globals.css`).toEqual([]);
    }
  });

  it("the <=720px arm takes the icon plate out with `display: none`, never a hidden box", () => {
    // The /sparade plate is a link: a box that is only invisible keeps a focus stop on nothing
    // (WCAG 2.4.3, 2.4.7). design-reviewer's condition 5 in #1875's form check.
    const plate = readRules(GLOBALS).filter(
      (r) => r.selector === ".jp-job--icon > .jp-job__match" && r.inMedia?.includes("max-width: 720px")
    );
    expect(plate.map((r) => r.properties)).toEqual([["display"]]);
    expect(declared(GLOBALS, ".jp-job--icon > .jp-job__match", "display")).toEqual(["none"]);
  });

  it("the shared row title wraps a word that does not fit, and only then", () => {
    // `anywhere`, not `break-word`: the title is a flex container, and `break-word` does not lower a
    // flex item's min-content, so it never breaks inside one. No `hyphens`: that re-breaks words that
    // would otherwise wrap whole, so it is not inert when the word fits.
    expect(declared(GLOBALS, ".jp-job__title", "overflow-wrap")).toEqual(["anywhere"]);
    expect(declared(GLOBALS, ".jp-app__title", "overflow-wrap")).toEqual(["anywhere"]);
    expect(declared(GLOBALS, ".jp-job__title", "hyphens")).toEqual([]);
  });
});

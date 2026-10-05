import { readdirSync, readFileSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";
import { readRules, type Rule } from "@/test/css-rules";
import { classTokens, sourceFiles } from "@/test/jsx-class-tokens";

/**
 * #1875 — a list row's column template lives in the stylesheet, never in an inline style.
 *
 * The row chassis `.jp-job, .jp-app` (`globals.css`) is a grid whose <=720px arm drops `.jp-job` to
 * one column, so the actions fall under the text. An inline `style` outranks that arm, so a row that
 * pinned its columns inline kept them at 320px and its button or match chip painted over the title —
 * on /sokningar, /matchningar and /sparade. Nothing could see it: jsdom applies no stylesheet, and
 * `guard:css` never reads `style=`.
 *
 * Two halves of one contract, both computed from the source tree and the stylesheets, never from a
 * list:
 *   1. no chassis element (`jp-job`, `jp-app`, or one of their `__parts`) carries an inline grid
 *      property, the icon plate carries no inline `display`, and a `style` or spread the scan cannot
 *      read counts against it (fail closed);
 *   2. outside a `max-width: 720px` block, the only rules that set a grid property on a `.jp-job` row
 *      or part are single-class `.jp-job`/`.jp-job--*` rules above the arm in `globals.css`. A media
 *      query adds no specificity, so source order decides (see `globals-pagehero-cascade.test.ts`).
 *
 * Known reach: a className held in a variable, composed at runtime, created with `createElement`, or
 * kept as a constant in a `.ts` file is invisible to a static scan.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(HERE, "..");
const GLOBALS_PATH = resolve(HERE, "globals.css");
const GLOBALS = readFileSync(GLOBALS_PATH, "utf8");

const toPosix = (p: string) => p.split(sep).join("/");

/** The chassis blocks and their parts. `jp-jobs`, `jp-job-tags` and `jp-job-skeleton` are other blocks. */
const isChassis = (token: string) => /^jp-(job|app)(__[a-z0-9-]+)?$/.test(token);

const normalised = (key: string) => key.replace(/-/g, "").toLowerCase();

/** `gridTemplateColumns`, `"grid-template-columns"` and `gridAutoFlow` all name a grid property. */
const isGridKey = (key: string) => normalised(key).startsWith("grid");

type Finding = { line: number; why: string };

function scan(file: string, text: string): { chassis: number; rows: number; findings: Finding[] } {
  const source = ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    /* setParentNodes — getStart(source) is passed explicitly */ false,
    ts.ScriptKind.TSX
  );
  const at = (node: ts.Node) => source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;

  let chassis = 0;
  let rows = 0;
  const findings: Finding[] = [];

  const check = (attrs: ts.JsxAttributes) => {
    const classes = classTokens(attrs);
    if (![...classes].some(isChassis)) return;
    chassis++;
    if (classes.has("jp-job")) rows++;

    let style: ts.JsxAttribute | undefined;
    for (const a of attrs.properties) {
      if (ts.isJsxSpreadAttribute(a)) findings.push({ line: at(a), why: "spread attribute" });
      else if (ts.isIdentifier(a.name) && a.name.text === "style") style = a;
    }
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
      const platesDisplay = classes.has("jp-job__match") && normalised(key) === "display";
      if (isGridKey(key) || platesDisplay) findings.push({ line: at(prop), why: key });
    }
  };

  const walk = (node: ts.Node): void => {
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) check(node.attributes);
    ts.forEachChild(node, walk);
  };
  walk(source);
  return { chassis, rows, findings };
}

/** Every stylesheet under `dir` that is not a CSS module: the ones that can reach a `.jp-job` row. */
function globalSheets(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = resolve(dir, entry.name);
    if (entry.isDirectory()) globalSheets(child, acc);
    else if (entry.name.endsWith(".css") && !entry.name.endsWith(".module.css")) acc.push(child);
  }
  return acc;
}

/** The selector's subject: its last compound, after the last combinator outside brackets. */
function subjectOf(selector: string): string {
  let depth = 0;
  for (let i = selector.length - 1; i >= 0; i--) {
    const c = selector[i];
    if (c === ")" || c === "]") depth++;
    else if (c === "(" || c === "[") depth--;
    else if (depth === 0 && (c === " " || c === ">" || c === "+" || c === "~")) return selector.slice(i + 1);
  }
  return selector;
}

/** The subject is a `.jp-job` row, part or modifier — not `.jp-jobs`, `.jp-job-tags` or `.jp-job-skeleton`. */
const subjectIsJob = (selector: string) =>
  (subjectOf(selector).match(/\.[\w-]+/g) ?? []).some(
    (c) => c === ".jp-job" || c.startsWith(".jp-job__") || c.startsWith(".jp-job--")
  );

const inNarrowArm = (r: Rule) => r.inMedia === "@media (max-width: 720px)";

/**
 * Every rule that can keep a `.jp-job` row's columns below 720px: it sets a grid property on a
 * `.jp-job` row or part outside a `max-width: 720px` block, and it is not a single-class
 * `.jp-job`/`.jp-job--*` rule above the arm in `globals.css`.
 */
function rulesThatBeatTheArm(globals: string, later: { name: string; css: string }[]): string[] {
  const rules = readRules(globals);
  const arm = rules.find(
    (r) => r.selector === ".jp-job" && inNarrowArm(r) && r.properties.includes("grid-template-columns")
  );
  if (!arm) return ["globals.css: no <=720px `.jp-job` arm"];
  const setsGrid = (r: Rule) => subjectIsJob(r.selector) && r.properties.some(isGridKey) && !inNarrowArm(r);
  const aboveTheArm = (r: Rule) =>
    r.inMedia === null && /^\.jp-job(--[a-z0-9-]+)?$/.test(r.selector) && r.start < arm.start;
  const out = rules
    .filter((r) => setsGrid(r) && !aboveTheArm(r))
    .map((r) => `globals.css: ${r.selector}${r.inMedia ? ` in ${r.inMedia}` : ""}`);
  for (const sheet of later) {
    out.push(...readRules(sheet.css).filter(setsGrid).map((r) => `${sheet.name}: ${r.selector}`));
  }
  return out;
}

/** Every value `property` is declared with, in every rule whose selector is `selector`. */
const declared = (css: string, selector: string, property: string): string[] =>
  readRules(css)
    .filter((r) => r.selector === selector)
    .flatMap((r) => r.declarations.filter((d) => d.property === property).map((d) => d.value));

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

  it("forbids an inline `display` on the icon plate, and nowhere else", () => {
    expect(
      findingsIn('const a = <div className="jp-job__match jp-job__match--neutral" style={{ display: "block" }} />;')
    ).toEqual(["display"]);
    expect(findingsIn('const a = <div className="jp-job__meta" style={{ display: "block" }} />;')).toEqual([]);
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
        expect(result.rows, "saved-job-ad-row renders a row in both branches").toBe(2);
      }
      for (const f of result.findings) offenders.push(`${rel}:${f.line} — ${f.why}`);
    }
    expect(chassis, "the scan found almost no chassis elements, so the rule below is vacuous").toBeGreaterThanOrEqual(10);

    expect(
      offenders,
      "an inline grid property, or the plate's inline `display`, outranks the <=720px arm, so the row " +
        "keeps its columns below 720px and the actions paint over the title. Give the row a " +
        "`.jp-job--*` modifier in globals.css, placed before the <=720px arm, instead."
    ).toEqual([]);
  });

  it("reads a selector's subject as a `.jp-job` row, part or modifier, and nothing else", () => {
    const selectors = [
      ".jp-job",
      ".jp-job__actions",
      ".jp-job--icon",
      ".jp-jobs > .jp-job--probe",
      ".jp-job:hover",
      ".jp-jobs",
      ".jp-job-tags",
      ".jp-job-skeleton",
      ".jp-job--icon > .jp-icon-btn",
    ];
    expect(selectors.filter(subjectIsJob)).toEqual([
      ".jp-job",
      ".jp-job__actions",
      ".jp-job--icon",
      ".jp-jobs > .jp-job--probe",
      ".jp-job:hover",
    ]);
  });

  it("reports every rule that can keep a row's columns below 720px, and none that cannot", () => {
    const base = ".jp-job, .jp-app { display: grid; grid-template-columns: 1fr auto; }\n";
    const arm = "@media (max-width: 720px) { .jp-job { grid-template-columns: 1fr; } }\n";
    const rule = (selector: string) => `${selector} { grid-template-columns: auto 1fr auto; }\n`;
    const media = (query: string, selector: string) => `@media ${query} { ${rule(selector)} }\n`;
    const check = (css: string, later: string[] = []) =>
      rulesThatBeatTheArm(css, later.map((c, i) => ({ name: `later${i}.css`, css: c })));

    expect(check(base + rule(".jp-job--probe") + arm)).toEqual([]);
    expect(check(base + arm + media("(max-width: 720px)", ".jp-job--probe"))).toEqual([]);
    expect(check(base + arm + rule(".jp-jobs"))).toEqual([]);

    expect(check(base + arm + rule(".jp-job--probe"))).toEqual(["globals.css: .jp-job--probe"]);
    expect(check(base + arm + media("(max-width: 768px)", ".jp-job--probe"))).toEqual([
      "globals.css: .jp-job--probe in @media (max-width: 768px)",
    ]);
    expect(check(base + rule(".jp-job.jp-job--probe") + arm)).toEqual(["globals.css: .jp-job.jp-job--probe"]);
    expect(check(base + arm + rule(".jp-jobs > .jp-job--probe"))).toEqual(["globals.css: .jp-jobs > .jp-job--probe"]);
    expect(check(base + ".jp-job__actions { grid-column: 2; }\n" + arm)).toEqual(["globals.css: .jp-job__actions"]);
    expect(check(base + arm, [rule(".jp-job--probe")])).toEqual(["later0.css: .jp-job--probe"]);
    expect(check(base)).toEqual(["globals.css: no <=720px `.jp-job` arm"]);
  });

  it("no rule in any stylesheet can keep a `.jp-job` row's columns below 720px", () => {
    const later = globalSheets(SRC).filter((f) => f !== GLOBALS_PATH);
    const names = later.map((f) => toPosix(relative(SRC, f)));
    expect(names, "the walk no longer reaches the stylesheets that load after globals.css").toEqual(
      expect.arrayContaining(["app/(app)/app.css", "app/(admin)/admin.css"])
    );
    expect(
      rulesThatBeatTheArm(
        GLOBALS,
        later.map((f, i) => ({ name: names[i] ?? f, css: readFileSync(f, "utf8") }))
      ),
      "a media query adds no specificity: a rule like these wins over the <=720px arm, so the row " +
        "never drops to one column. Use a single-class `.jp-job--*` rule above the arm."
    ).toEqual([]);
  });

  it("the <=720px arm takes the icon plate out with `display: none`, never a hidden box", () => {
    // The /sparade plate is a link: a box that is only invisible keeps a focus stop on nothing
    // (WCAG 2.4.3, 2.4.7). design-reviewer's condition 5 in #1875's form check.
    const plate = readRules(GLOBALS).filter(
      (r) => r.selector === ".jp-job--icon > .jp-job__match" && inNarrowArm(r)
    );
    expect(plate.map((r) => r.declarations)).toEqual([[{ property: "display", value: "none" }]]);
  });

  it("the shared row title wraps a word that does not fit, and only then", () => {
    // `anywhere`, not `break-word`: the title is a flex container, and `break-word` does not lower a
    // flex item's min-content, so it never breaks inside one. No `hyphens`: that re-breaks words that
    // would otherwise wrap whole, so it is not inert when the word fits.
    for (const title of [".jp-job__title", ".jp-app__title"]) {
      expect(declared(GLOBALS, title, "overflow-wrap"), title).toEqual(["anywhere"]);
      expect(declared(GLOBALS, title, "hyphens"), title).toEqual([]);
    }
  });
});

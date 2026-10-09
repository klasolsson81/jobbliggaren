import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { FEEDBACK_ROUTES } from "@/lib/feedback/page-keys";
import { stripComments } from "@/lib/layout/strip-comments";

/**
 * Every page under `(app)` is classified for feedback (#1979 PR3, ADR 0156 D1), and each mapped page
 * carries exactly one rating row with its own key. The route set is DERIVED from the page files on disk,
 * never listed, so a new page cannot go unclassified, and a page cannot quietly lose or duplicate its row.
 *
 * It reads source text with the comments stripped: it proves the row is written into the page, not
 * that a given render reaches it. Which branch it sits in is the page's own review.
 */
const APP = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(APP, "../..");

const IMPORT = /from\s+"@\/components\/feedback\/page-feedback"/;
const ROW = /<PageFeedback\b/g;

type PageFile = { readonly pattern: string; readonly file: string; readonly code: string };

/** Every `page.tsx` under `(app)` outside parallel-route slots and private folders, with its route. */
function pageFiles(dir = APP, segments: string[] = []): PageFile[] {
  const out: PageFile[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name.startsWith("@") || entry.name.startsWith("_")) continue;
      // A route group contributes no URL segment.
      const next = entry.name.startsWith("(") ? segments : [...segments, entry.name];
      out.push(...pageFiles(full, next));
    } else if (entry.name === "page.tsx") {
      out.push({ pattern: "/" + segments.join("/"), file: full, code: stripComments(readFileSync(full, "utf-8")) });
    }
  }
  return out;
}

/** Every non-test source file under `src/`. */
function sourceFiles(dir = SRC): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...sourceFiles(full));
    else if (/\.(ts|tsx)$/.test(entry.name) && !/\.(test|spec)\.(ts|tsx)$/.test(entry.name)) out.push(full);
  }
  return out;
}

const pages = pageFiles();
const routeOf = new Map(FEEDBACK_ROUTES.map((route) => [route.pattern, route]));

describe("(app) — feedback coverage of every page", () => {
  it("finds the page files (the guard is not vacuously green)", () => {
    expect(pages.length).toBeGreaterThanOrEqual(30);
  });

  it("classifies exactly the routes on disk", () => {
    expect(pages.map((page) => page.pattern).sort()).toEqual(FEEDBACK_ROUTES.map((route) => route.pattern).sort());
  });

  it.each(pages.map((page) => [page.pattern, page] as const))("%s carries the row its classification says", (_pattern, page) => {
    const route = routeOf.get(page.pattern);
    expect(route, `${page.pattern} is not in FEEDBACK_ROUTES`).toBeDefined();
    const rows = page.code.match(ROW) ?? [];

    if (route !== undefined && "key" in route) {
      expect(rows, `${page.file} must render exactly one <PageFeedback>`).toHaveLength(1);
      expect(page.code).toMatch(new RegExp(`<PageFeedback\\s+pageKey="${route.key}"\\s*/>`));
      expect(page.code).toMatch(IMPORT);
      return;
    }

    expect(rows, `${page.file} is exempt and must render no <PageFeedback>`).toHaveLength(0);
    // An exempt page renders no product page: it navigates away instead.
    const navigatesAway = route !== undefined && route.exempt.includes("notFound()") ? /\bnotFound\(\)/ : /\bredirect\(/;
    expect(page.code).toMatch(navigatesAway);
  });

  it("renders the row nowhere but in an (app) page file, and never in a modal slot", () => {
    const pageSet = new Set(pages.map((page) => page.file));
    const rendering = sourceFiles()
      .filter((file) => (stripComments(readFileSync(file, "utf-8")).match(ROW) ?? []).length > 0)
      .map((file) => relative(SRC, file).split(sep).join("/"));
    // Bites on a reader that finds nothing: the mapped pages themselves are in the set.
    expect(rendering.length).toBeGreaterThan(0);
    expect(rendering.filter((file) => file.includes("/@"))).toEqual([]);
    expect(rendering.filter((file) => !pageSet.has(resolve(SRC, file)))).toEqual([]);
  });
});

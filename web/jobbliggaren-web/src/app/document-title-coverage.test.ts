import { existsSync, readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * Fitness function for repo-wide document-title coverage (WCAG 2.4.2, level A).
 *
 * The defect it closes (design-reviewer Blocker 1 on #1495, follow-up debt 2):
 * measured over HTTP against a production build at `89ad3de3`, `/`, `/logga-in`,
 * `/registrera`, `/gast/oversikt` and every unmatched URL all served
 * `<title>Jobbliggaren</title>`. Five documents with five different purposes read
 * identically, so the title described none of them. Only 15 of the app's pages set a
 * title of their own.
 *
 * The invariant asserted here is deliberately simple: EVERY document has a title. The
 * set is DERIVED from the filesystem, never listed — a list is the silent hole
 * `modal-slot-coverage.test.ts` names, and both it and `protected-routes.test.ts`
 * prove the derivation idiom on this directory.
 *
 * Two kinds of file are not documents: parallel-route slots (a `@`-prefixed segment
 * contributes no URL and renders inside another page's document) and the single entry
 * in the allowlist below.
 *
 * NOT covered, and the reason is measured rather than assumed: `not-found.tsx` files.
 * The root and guest boundaries do carry a title (via `notFoundMetadata()`), but
 * `(app)/not-found.tsx` measurably cannot — it is reached by a `notFound()` thrown
 * mid-stream, so the head has already flushed and its own metadata never applies.
 * Covering not-found files would therefore need a second kind of exclusion, and the
 * point of this test is that it has exactly one.
 */
const APP = resolve(dirname(fileURLToPath(import.meta.url)));

/**
 * Pages that are never served as a titled document of their own. Every entry carries
 * its reason; an entry whose reason stops holding is a page that needs a title.
 */
const NOT_A_DOCUMENT: ReadonlyArray<{ file: string; reason: string }> = [
  {
    file: "(marketing)/page.tsx",
    reason:
      "The site root is the one document `title.default` describes. It serves `Jobbliggaren` by decision (CTO bind 2026-08-24), not by omission.",
  },
];

/** A path segment starting with `@` is a parallel-route slot: no URL, no document. */
const isSlotSegment = (segment: string): boolean => segment.startsWith("@");

function pageFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      pageFiles(full, acc);
    } else if (entry.name === "page.tsx") {
      acc.push(full);
    }
  }
  return acc;
}

/**
 * A module declares a title if it exports metadata AND that metadata carries a title.
 * The second half is load-bearing: three `(auth)` pages exported `metadata` for
 * `robots`/`referrer` and no `title` at all, and a check for the export alone counted
 * them as covered when they were not.
 *
 * `notFoundMetadata()` is the second admissible source: the 404 surfaces take one
 * shared title from `lib/metadata/not-found-title.ts` rather than repeating it.
 */
function declaresATitle(source: string): boolean {
  const exportsMetadata =
    /export const metadata\b/.test(source) ||
    /export (async )?function generateMetadata\b/.test(source);
  if (!exportsMetadata) return false;
  return /\btitle:/.test(source) || /\bnotFoundMetadata\(/.test(source);
}

describe("document title coverage", () => {
  const documents = pageFiles(APP)
    .map((file) => relative(APP, file).split(sep).join("/"))
    .filter((file) => !file.split("/").some(isSlotSegment))
    .filter((file) => !NOT_A_DOCUMENT.some((entry) => entry.file === file));

  it("derives the document-producing pages", () => {
    // Guards the derivation itself: a walk that silently found nothing would make
    // every assertion below vacuously true.
    expect(documents.length).toBeGreaterThan(40);
  });

  it.each(documents)("%s sets a document title", (file) => {
    expect(declaresATitle(readFileSync(join(APP, file), "utf8"))).toBe(true);
  });

  it.each(NOT_A_DOCUMENT.map((entry) => entry.file))(
    "%s still exists, so its exclusion still refers to something",
    (file) => {
      expect(existsSync(join(APP, file))).toBe(true);
    }
  );
});

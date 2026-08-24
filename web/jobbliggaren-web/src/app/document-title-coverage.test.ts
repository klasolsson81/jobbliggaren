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
 * Two invariants, because the defect had two halves. Every document HAS a title, and
 * no two documents with different subjects SHARE one — presence alone would have let
 * the original defect back in one page at a time. Both sets are DERIVED from the
 * filesystem, never listed: a list is the silent hole `modal-slot-coverage.test.ts`
 * names, and both it and `protected-routes.test.ts` prove the derivation idiom here.
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
const MESSAGES = resolve(APP, "..", "..", "messages", "sv");

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

/**
 * The one title several documents may legitimately share. The root 404, the guest 404
 * and the six retired CV routes are one document semantically — same copy, same
 * purpose, and they can never stand open as different subjects — so they take one
 * title from `lib/metadata/not-found-title.ts` rather than repeating the string.
 */
const SHARED_NOT_FOUND_TITLE = "<notFoundMetadata()>";

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
 * The source of the file's metadata export, and nothing else.
 *
 * Scoping matters more than it looks. A `title:` ANYWHERE in the file — an
 * `ErrorShell({ title, body })` helper's prop type, a DTO mapping, a section lookup —
 * would satisfy a file-wide search, and pages do carry such a `title:` outside their
 * metadata. A file-wide predicate therefore passes them with
 * the metadata title removed, which is precisely the defect this test exists to catch:
 * three `(auth)` pages really did export `metadata` for `robots`/`referrer` and no
 * title at all.
 *
 * The block ends at the first line that closes at column zero (`}` or `};`), which is
 * what the repo's formatting guarantees for a top-level export and what makes this a
 * scan rather than a parser.
 */
function metadataBlock(source: string): string | null {
  const lines = source.split(/\r?\n/);
  const start = lines.findIndex((line) =>
    /^export (?:const metadata\b|(?:async )?function generateMetadata\b)/.test(line)
  );
  if (start === -1) return null;

  const end = lines.findIndex(
    (line, index) => index > start && /^\};?$/.test(line)
  );
  if (end === -1) return null;

  return lines.slice(start, end + 1).join("\n");
}

/**
 * A module declares a title if it exports metadata AND that metadata carries a title.
 * The second half is load-bearing, for the reason `metadataBlock` records.
 *
 * `notFoundMetadata()` is the second admissible source: it returns a title, so a block
 * that delegates to it carries one without naming it.
 */
function declaresATitle(source: string): boolean {
  const block = metadataBlock(source);
  if (block === null) return false;
  return /\btitle:/.test(block) || /\bnotFoundMetadata\(/.test(block);
}

/** Reads one dotted path out of the Swedish catalogue, which is the copy's source. */
function message(path: string): string {
  const [file, ...rest] = path.split(".");
  let node: unknown = JSON.parse(
    readFileSync(join(MESSAGES, `${file}.json`), "utf8")
  );
  for (const key of rest) {
    if (typeof node !== "object" || node === null || !(key in node)) {
      throw new Error(`no message at ${path} (stopped at ${key})`);
    }
    node = (node as Record<string, unknown>)[key];
  }
  if (typeof node !== "string") throw new Error(`${path} is not a string`);
  return node;
}

/**
 * The Swedish string a page's document title resolves to, so the distinctness check
 * compares what a reader actually sees rather than which key was written.
 *
 * It throws rather than returning a fallback: a page whose title cannot be resolved is
 * a hole in the check, and a hole that reports itself as a pass is the failure mode
 * this whole file is about.
 */
function resolveTitle(source: string): string {
  const block = metadataBlock(source);
  if (block === null) throw new Error("no metadata export");
  if (/\bnotFoundMetadata\(/.test(block)) return SHARED_NOT_FOUND_TITLE;

  const [, key] = /title:\s*t\("([^"]+)"\)/.exec(block) ?? [];
  if (key !== undefined) {
    const [, namespace] = /getTranslations\("([^"]+)"\)/.exec(block) ?? [];
    if (namespace === undefined) throw new Error(`title key ${key} has no namespace`);
    return message(`${namespace}.${key}`);
  }

  const [, literal] = /title:\s*"([^"]+)"/.exec(block) ?? [];
  if (literal !== undefined) return literal;

  throw new Error("metadata export carries no title");
}

describe("document title coverage", () => {
  const documents = pageFiles(APP)
    .map((file) => relative(APP, file).split(sep).join("/"))
    .filter((file) => !file.split("/").some(isSlotSegment))
    .filter((file) => !NOT_A_DOCUMENT.some((entry) => entry.file === file));

  const read = (file: string): string => readFileSync(join(APP, file), "utf8");

  it("derives the document-producing pages", () => {
    // Guards the derivation itself: a walk that silently found nothing would make
    // every assertion below vacuously true.
    expect(documents.length).toBeGreaterThan(40);
  });

  it("reads a title out of the metadata export, not out of the whole file", () => {
    // The control for `metadataBlock`. Without it the suite cannot tell a scoped
    // predicate from the file-wide one it replaced, because both pass on every real
    // page — the difference only shows on a page whose `title:` sits elsewhere.
    const titleOutsideMetadata = [
      'export const metadata: Metadata = {',
      '  robots: { index: false, follow: false },',
      '};',
      '',
      'function ErrorShell({ title }: { title: string }) {',
      '  return <h1>{title}</h1>;',
      '}',
    ].join("\n");

    expect(declaresATitle(titleOutsideMetadata)).toBe(false);
    expect(
      declaresATitle('export const metadata: Metadata = {\n  title: "x",\n};')
    ).toBe(true);
  });

  it.each(documents)("%s sets a document title", (file) => {
    expect(declaresATitle(read(file))).toBe(true);
  });

  it("gives no two documents with different subjects the same title", () => {
    const byTitle = new Map<string, string[]>();
    for (const file of documents) {
      const title = resolveTitle(read(file));
      byTitle.set(title, [...(byTitle.get(title) ?? []), file]);
    }
    // The site root is allowlisted out of the coverage check but not out of this one:
    // it serves `title.default`, and a page that repeats that string is exactly the
    // collision the original Blocker was about.
    const siteDefault = message("metadata.titleDefault");
    byTitle.set(siteDefault, [
      ...(byTitle.get(siteDefault) ?? []),
      "(marketing)/page.tsx",
    ]);

    expect(
      byTitle.get(SHARED_NOT_FOUND_TITLE)?.length ?? 0,
      "no page delegates to notFoundMetadata(), so the shared-title exemption below refers to nothing"
    ).toBeGreaterThan(1);

    const collisions = [...byTitle]
      .filter(([title, files]) => title !== SHARED_NOT_FOUND_TITLE && files.length > 1)
      .map(([title, files]) => `${title}: ${files.join(", ")}`);

    expect(
      collisions,
      "these documents have different subjects but read identically in a tab strip, " +
        "which is the defect this PR closed"
    ).toEqual([]);
  });

  it.each(NOT_A_DOCUMENT.map((entry) => entry.file))(
    "%s still exists, so its exclusion still refers to something",
    (file) => {
      expect(existsSync(join(APP, file))).toBe(true);
    }
  );
});

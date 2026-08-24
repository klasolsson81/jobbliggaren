/**
 * Reading a page's metadata export out of its SOURCE, for the two title fitness
 * functions that need it: `app/document-title-coverage.test.ts` (does every document
 * declare a title, and are they distinct) and
 * `app/(app)/detail-route-not-found-title.test.ts` (which routes can serve their own
 * title over a 404 body). Both ask a different question of the same construct, and where
 * a top-level metadata export begins and ends is one piece of knowledge, so it is
 * written once.
 */

/**
 * The source of the file's metadata export, and nothing else.
 *
 * Scoping matters more than it looks. A `title:` ANYWHERE in the file — an
 * `ErrorShell({ title, body })` helper's prop type, a DTO mapping, a section lookup —
 * would satisfy a file-wide search, and pages do carry such a `title:` outside their
 * metadata. A file-wide predicate therefore passes them with the metadata title removed,
 * which is precisely the defect these tests exist to catch: three `(auth)` pages really
 * did export `metadata` for `robots`/`referrer` and no title at all.
 *
 * The block ends at the first line that closes at column zero (`}` or `};`), which is
 * what the repo's formatting guarantees for a top-level export and what makes this a
 * scan rather than a parser.
 */
export function metadataBlock(source: string): string | null {
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
 * Whether the metadata export names a title OF ITS OWN, as opposed to delegating the
 * whole of its metadata to `notFoundMetadata()`.
 *
 * That distinction is the one both callers turn on. A page with a title of its own is a
 * document whose title must be distinct from every other subject's — and, if its render
 * can reach `notFound()`, a page that will serve that title over a body saying the
 * address does not exist unless it resolves against the record's absence.
 */
export function declaresOwnTitle(source: string): boolean {
  const block = metadataBlock(source);
  return block !== null && /\btitle:/.test(block);
}

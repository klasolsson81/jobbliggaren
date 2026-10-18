/**
 * The sitemaps.org XML the sitemap is made of (ADR 0174): an index, and URL sets whose entries carry `loc` and nothing
 * else (no `lastmod`, `changefreq` or `priority`). Next 16 writes no index and escapes nothing, so it is written here.
 * One entry per line, so `grep -c '<loc>'` counts entries. Pure, with no `next/*` import.
 */
export const SITEMAP_NAMESPACE = "http://www.sitemaps.org/schemas/sitemap/0.9";

/** Google's limit per sitemap file ("Build and submit a sitemap", updated 2026-07-08). */
export const MAX_URLS_PER_SITEMAP = 50_000;

const XML_ESCAPES: Readonly<Record<string, string>> = {
  "&": "&amp;",
  "<": "&lt;",
  ">": "&gt;",
  '"': "&quot;",
  "'": "&apos;",
};

export function escapeXml(value: string): string {
  return value.replace(/[&<>"']/g, (char) => XML_ESCAPES[char] ?? char);
}

const PROLOG = '<?xml version="1.0" encoding="UTF-8"?>';

function document(root: "urlset" | "sitemapindex", entry: "url" | "sitemap", locs: readonly string[]): string {
  if (locs.length > MAX_URLS_PER_SITEMAP) {
    throw new RangeError(`A sitemap file holds at most ${MAX_URLS_PER_SITEMAP} entries.`);
  }
  const lines = locs.map((loc) => `<${entry}><loc>${escapeXml(loc)}</loc></${entry}>`);
  return [PROLOG, `<${root} xmlns="${SITEMAP_NAMESPACE}">`, ...lines, `</${root}>`, ""].join("\n");
}

/** A URL set: the pages, one `loc` each. */
export function urlsetXml(locs: readonly string[]): string {
  return document("urlset", "url", locs);
}

/** A sitemap index: the sitemap files, one `loc` each. */
export function sitemapIndexXml(locs: readonly string[]): string {
  return document("sitemapindex", "sitemap", locs);
}

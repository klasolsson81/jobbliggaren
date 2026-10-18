import { absoluteUrl } from "@/lib/site-url";

/** The public catalogue's one path (ADR 0176 F1). Nothing lives below it in S5. */
export const PUBLIC_CATALOGUE_PATH = "/lediga-jobb" as const;

/**
 * A catalogue page's address on the site: page 1 is the bare path, every later page carries `?sida=n`. The pager's
 * links, the canonical and the sitemap entry all come from here, so they cannot differ.
 */
export function publicCatalogueHref(page: number): `/${string}` {
  return page <= 1 ? PUBLIC_CATALOGUE_PATH : `${PUBLIC_CATALOGUE_PATH}?sida=${page}`;
}

/** The absolute form of {@link publicCatalogueHref}: the page's canonical, and page 1's `loc` in the sitemap. */
export function publicCatalogueUrl(page: number): string {
  return absoluteUrl(publicCatalogueHref(page));
}

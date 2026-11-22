import { absoluteUrl } from "@/lib/site-url";
import { publicCatalogueUrl } from "@/lib/job-ads/public-catalogue-url";
import { publicJuniorUrl } from "@/lib/job-ads/junior-it-params";

/**
 * The marketing part of the sitemap (#264, ADR 0174): the canonical public pages of the `(marketing)` landing and the
 * `(marketing-inner)` content pages. Deliberately not listed: the authenticated app, the `/gast` demo, and the auth
 * utility pages. A new public marketing page is added here when it ships; `seo-canonical.test.ts` derives the public
 * pages from the filesystem and holds this list, with the catalogue's first page and `/junior`, equal to them.
 */
export const MARKETING_PATHS = [
  "/",
  "/om",
  "/kontakt",
  "/for-utvecklare",
  "/integritet",
  "/kontaktperson-i-annons",
  "/cookies",
  "/villkor",
  "/tillganglighet",
  "/hjalpcenter",
  "/tips",
  "/vanliga-fragor",
  "/matchning",
  "/cv-granskning",
] as const satisfies readonly `/${string}`[];

/**
 * The pages part's locations: the marketing pages in the order above, then the public catalogue's first page (ADR 0176
 * F8) and the junior IT list's indexable view (ADR 0166, the Amendment 2026-11-22), each built by the same function as
 * that page's canonical. No other page of either list is listed: a later page is a position in a moving list, not a
 * document.
 */
export function marketingSitemapLocs(): string[] {
  return [...MARKETING_PATHS.map((path) => absoluteUrl(path)), publicCatalogueUrl(1), publicJuniorUrl()];
}

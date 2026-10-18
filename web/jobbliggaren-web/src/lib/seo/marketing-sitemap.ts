import { absoluteUrl } from "@/lib/site-url";

/**
 * The marketing part of the sitemap (#264, ADR 0174): the canonical public pages of the `(marketing)` landing and the
 * `(marketing-inner)` content pages. Deliberately not listed: the authenticated app, the `/gast` demo, and the auth
 * utility pages. A new public marketing page is added here when it ships; `seo-canonical.test.ts` derives the public
 * pages from the filesystem and holds this list equal to them.
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

/** The marketing pages as absolute URLs, in the order above. */
export function marketingSitemapLocs(): string[] {
  return MARKETING_PATHS.map((path) => absoluteUrl(path));
}

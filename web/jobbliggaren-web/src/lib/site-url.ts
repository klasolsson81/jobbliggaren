/**
 * The canonical absolute base URL of the public site, used by the SEO routes
 * (`app/robots.ts`, `app/sitemap.ts`) to emit absolute URLs. Mirrors the
 * `metadataBase` fallback in `app/layout.tsx` (single convention): production sets
 * `NEXT_PUBLIC_SITE_URL`; the dev fallback keeps local/dev builds valid.
 *
 * Kept as its own module so robots + sitemap share ONE source (DRY) without
 * importing the root layout. No trailing slash — callers append the path.
 */
export const SITE_URL =
  process.env.NEXT_PUBLIC_SITE_URL ?? "https://dev.jobbliggaren.se";

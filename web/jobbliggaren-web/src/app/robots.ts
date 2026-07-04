import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site-url";
import { PROTECTED_PREFIXES } from "@/lib/auth/protected-routes";

/**
 * SEO foundation (#264) — `app/robots.ts` (Next.js App Router metadata route → /robots.txt).
 *
 * Allows crawling of the public marketing surface and points at the sitemap; DISALLOWS the
 * authenticated app, the API, the admin surface, and the demo sandbox.
 *
 * The authed `(app)` prefixes are DERIVED from `PROTECTED_PREFIXES` — the single source of truth,
 * frozen to the `(app)` route group by `protected-routes.test.ts` (#513) — so this disallow list
 * can never drift from the middleware gate again. The remaining entries are robots-local and are
 * NOT `(app)` routes:
 *   - `/api/`  — the API surface.
 *   - `/gast`  — the logged-out demo sandbox: it mirrors the authed app shell with demo data and is
 *                not canonical content (avoids thin/duplicate indexing).
 *   - `/admin` — the admin surface.
 *
 * NB: prefixes like `/jobb` and `/cv` are the AUTHED routes; the public job search is the demo
 * `/gast/jobb` (under the separately-disallowed `/gast`), so disallowing `/jobb` does not block a
 * public page. New authed areas are picked up automatically via `PROTECTED_PREFIXES`.
 */
const ROBOTS_LOCAL_DISALLOW = ["/api/", "/gast", "/admin"] as const;

export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: [...ROBOTS_LOCAL_DISALLOW, ...PROTECTED_PREFIXES],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}

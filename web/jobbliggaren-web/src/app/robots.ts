import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site-url";

/**
 * SEO foundation (#264) — `app/robots.ts` (Next.js App Router metadata route → /robots.txt).
 *
 * Allows crawling of the public marketing surface and points at the sitemap; DISALLOWS the
 * authenticated app and the API. The disallow list is the real authed `(app)` URL prefixes:
 * `middleware.ts` PROTECTED_PREFIXES PLUS the remaining `(app)` routes not yet in that list
 * (`/matchningar`, `/aktivitetsrapport`, `/ny-ansokan`) and `/admin` — private user areas must
 * never be indexed. (We deliberately drop middleware's stale `/mig` prefix — that route no longer
 * exists; it was replaced by `/installningar`, which IS listed.) `/gast` (the logged-out demo
 * sandbox) is also disallowed: it mirrors the authed app shell with demo data and is not canonical
 * content (avoids thin/duplicate indexing).
 *
 * NB: prefixes like `/jobb` and `/cv` are the AUTHED routes; the public job search is the demo
 * `/gast/jobb` (under the separately-disallowed `/gast`), so disallowing `/jobb` does not block a
 * public page. Add new authed prefixes here when `(app)` grows.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: [
        "/api/",
        "/gast",
        "/oversikt",
        "/jobb",
        "/ansokningar",
        "/cv",
        "/installningar",
        "/sokningar",
        "/sparade",
        "/matchningar",
        "/aktivitetsrapport",
        "/ny-ansokan",
        "/admin",
      ],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}

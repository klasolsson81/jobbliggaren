import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site-url";
import { PROTECTED_PREFIXES } from "@/lib/auth/protected-routes";

/**
 * SEO foundation (#264) — `app/robots.ts` (Next.js App Router metadata route → /robots.txt).
 *
 * Allows crawling of the public marketing surface and points at the sitemap; DISALLOWS the
 * authenticated app, the API, the admin surface, and the demo sandbox.
 *
 * The authed `(app)` disallow entries are DERIVED from `PROTECTED_PREFIXES` — the single source of
 * truth, frozen to the `(app)` route group by `protected-routes.test.ts` (#513) — so this list can
 * never drift from the middleware gate.
 *
 * Boundary encoding (#583): each authed prefix P is emitted as BOTH `P$` (exact) and `P/` (subtree),
 * mirroring the middleware gate's segment-boundary match `pathname === P || pathname.startsWith(P + "/")`
 * (the `isProtectedPath` predicate, added in the middleware half, PR #598).
 * A bare `Disallow: /cv` uses prefix semantics and would ALSO shadow the PUBLIC `/cv-granskning`
 * marketing page (which `sitemap.ts` lists as canonical) — the #583 bug. `/cv$` + `/cv/` block `/cv`
 * and `/cv/...` while leaving `/cv-granskning` (and any future public sibling that shares an authed
 * prefix) crawlable. The `$` end-anchor and `/` boundary are honoured by Google/Bing/Yandex (RFC
 * 9309) and degrade safely: a `$`-unaware crawler treats `/cv$` as a literal no-op and still never
 * matches `/cv-granskning`.
 *
 * The robots-local entries are NOT `(app)` routes and are scoped to their subtree (trailing `/`) for
 * the same reason — a bare prefix could shadow a public sibling of the same latent class:
 *   - `/api/`   — the API surface.
 *   - `/gast/`  — the logged-out demo sandbox: mirrors the authed app shell with demo data, not
 *                 canonical content (avoids thin/duplicate indexing). No bare `/gast` page exists.
 *   - `/admin/` — the admin surface. No bare `/admin` page exists.
 */
const ROBOTS_LOCAL_DISALLOW = ["/api/", "/gast/", "/admin/"] as const;

export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: [
        ...ROBOTS_LOCAL_DISALLOW,
        ...PROTECTED_PREFIXES.flatMap((prefix) => [`${prefix}$`, `${prefix}/`]),
      ],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}
